using System.Net.Http.Headers;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>The injectable seams of the update check; production leaves everything null.</summary>
internal sealed record UpdateCheckSeams
{
    /// <summary>Where the one-day answer lives; null uses <c>update-check.json</c> in the state root.</summary>
    public string? CachePath { get; init; }

    /// <summary>The wall clock the cache's freshness reads; null uses UTC now.</summary>
    public Func<DateTimeOffset>? Clock { get; init; }

    /// <summary>The transport behind the request; null uses the platform <see cref="HttpClientHandler"/>.</summary>
    public HttpMessageHandler? Handler { get; init; }

    /// <summary>The releases endpoint; null uses <see cref="UpdateCheck.DefaultEndpoint"/>.</summary>
    public string? Endpoint { get; init; }
}

/// <summary>The frontend asking; only the interactive session may ever check.</summary>
internal enum UpdateCheckMode
{
    Interactive,
    Print,
    Acp,
}

/// <summary>The newest published release: its version without a leading <c>v</c> and its changelog URL.</summary>
internal sealed record UpdateInfo(string Version, string ChangelogUrl);

/// <summary>
/// The daily GitHub releases check: one request per day, cached under the state root and reused
/// until it expires, run only in the background of an interactive session. A check is an
/// announcement and never a session prerequisite: every failure is silent and returns null.
/// </summary>
internal static class UpdateCheck
{
    internal const string DefaultEndpoint =
        "https://api.github.com/repos/wdaniel1993/lunate/releases/latest";

    internal const string FallbackChangelogUrl =
        "https://github.com/wdaniel1993/lunate/blob/main/CHANGELOG.md";

    internal static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The cached answer when it is less than a day old; a stale or missing cache is null.</summary>
    internal static UpdateInfo? Cached(UpdateCheckSeams? seams, DateTimeOffset now)
    {
        try
        {
            return UpdateCheckCache.Read(CachePath(seams)) is { } cache && cache.IsFresh(now)
                ? cache.Info
                : null;
        }
        catch (Exception)
        {
            // A cache read is never a session prerequisite, not even when the path itself fails.
            return null;
        }
    }

    /// <summary>The release when it is newer than <paramref name="currentVersion"/>; null otherwise.</summary>
    internal static UpdateInfo? IsNewer(UpdateInfo? info, string currentVersion)
    {
        if (
            info is null
            || !SemanticVersion.TryParse(info.Version, out SemanticVersion latest)
            || !SemanticVersion.TryParse(currentVersion, out SemanticVersion current)
        )
        {
            return null;
        }

        return latest.CompareTo(current) > 0 ? info : null;
    }

    /// <summary>The seams' clock; the session and the cache freshness must read the same one.</summary>
    internal static DateTimeOffset Now(UpdateCheckSeams? seams) =>
        (seams?.Clock ?? (() => DateTimeOffset.UtcNow))();

    /// <summary>
    /// The one check path: a fresh cache makes no request; otherwise an exclusive sidecar lock
    /// serializes the attempt across processes, a reservation entry consumes the day before the
    /// request, and the answer (nulls on failure) replaces it afterwards. Never throws.
    /// </summary>
    internal static async Task<UpdateInfo?> CheckAsync(
        UpdateCheckSeams? seams,
        CancellationToken ct = default
    )
    {
        string path = CachePath(seams);
        DateTimeOffset now = Now(seams);
        try
        {
            if (UpdateCheckCache.Read(path) is { } cached && cached.IsFresh(now))
            {
                return cached.Info;
            }

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            FileStream? lockFile;
            try
            {
                lockFile = new FileStream(
                    path + ".lock",
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
            }
            catch (IOException)
            {
                // Another process is checking right now; this one joins the next day's answer.
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            using (lockFile)
            {
                if (UpdateCheckCache.Read(path) is { } locked && locked.IsFresh(now))
                {
                    return locked.Info;
                }

                UpdateCheckCache.Write(path, new UpdateCheckCache(now, null, null));
                if (ct.IsCancellationRequested)
                {
                    return null;
                }

                UpdateInfo? info = await FetchAsync(seams, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return null;
                }

                UpdateCheckCache.Write(
                    path,
                    new UpdateCheckCache(now, info?.Version, info?.ChangelogUrl)
                );
                return info;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<UpdateInfo?> FetchAsync(UpdateCheckSeams? seams, CancellationToken ct)
    {
        try
        {
            using HttpClient client = seams?.Handler is { } handler
                ? new HttpClient(handler, disposeHandler: false)
                : new HttpClient();
            client.Timeout = RequestTimeout;
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                seams?.Endpoint ?? DefaultEndpoint
            );
            request.Headers.UserAgent.Add(
                new ProductInfoHeaderValue("lunate", ProductVersion.Display)
            );
            using HttpResponseMessage response = await client
                .SendAsync(request, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using Stream body = await response
                .Content.ReadAsStreamAsync(ct)
                .ConfigureAwait(false);
            using JsonDocument document = await JsonDocument
                .ParseAsync(body, cancellationToken: ct)
                .ConfigureAwait(false);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? version = NormalizeVersion(Text(root, "tag_name"));
            if (version is null)
            {
                return null;
            }

            return new UpdateInfo(version, NormalizeUrl(Text(root, "html_url")));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Offline, rate-limited, malformed and timed out are all the same silent answer.
            return null;
        }
    }

    private static string CachePath(UpdateCheckSeams? seams) =>
        seams?.CachePath ?? Path.Combine(LunateHome.Store, "update-check.json");

    private static string? Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>The tag without one leading <c>v</c>/<c>V</c>; null when it is absent or not a version.</summary>
    private static string? NormalizeVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        string version = tag.Trim();
        if (!SemanticVersion.TryParse(version, out _))
        {
            return null;
        }

        return version.StartsWith('v') || version.StartsWith('V') ? version[1..] : version;
    }

    /// <summary>The release's own page when it is absolute http(s); the changelog otherwise.</summary>
    private static string NormalizeUrl(string? url)
    {
        if (
            !string.IsNullOrWhiteSpace(url)
            && Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
        )
        {
            return url;
        }

        return FallbackChangelogUrl;
    }
}

/// <summary>The policy matrix: interactive only, on unless opted out, offline or CI.</summary>
internal static class UpdateCheckPolicy
{
    internal static bool Enabled(
        AgentSettings settings,
        Func<string, string?> environment,
        LaunchOptions launch,
        UpdateCheckMode mode
    )
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(launch);

        if (mode is not UpdateCheckMode.Interactive || !settings.UpdateCheck || launch.Offline)
        {
            return false;
        }

        string? ci = environment("CI");
        if (!string.IsNullOrWhiteSpace(ci))
        {
            string value = ci.Trim();
            if (
                !string.Equals(value, "0", StringComparison.Ordinal)
                && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            )
            {
                return false;
            }
        }

        return true;
    }
}
