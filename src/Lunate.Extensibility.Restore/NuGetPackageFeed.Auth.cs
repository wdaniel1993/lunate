using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using NuGet.Configuration;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// The authentication half of the feed seam (spec: "Failure, offline and private feeds", issue
/// #244): a private feed that refuses its client does not become "could not be downloaded" with
/// the 401 buried three exceptions deep. A failure whose chain carries a 401 or 403 status is
/// re-raised as the one readable diagnosis of that case — the feed, the status, the configuration
/// file that can carry its <c>packageSourceCredentials</c>, and the authentication plugin a feed
/// that mints short-lived tokens would use instead. Every other failure keeps its original shape;
/// only the authentication case is recognised and answered.
/// </summary>
internal sealed partial class NuGetPackageFeed
{
    /// <summary>
    /// The failed request's status is what NuGet's chain carries — <c>FatalProtocolException</c>
    /// wrapping the HttpClient's <c>HttpRequestException</c> — and the message's own invariant text
    /// is the fallback for the shapes without a typed status.
    /// </summary>
    private static bool IsAuthenticationFailure(Exception exception) =>
        StatusOf(exception) is 401 or 403;

    private static int? StatusOf(Exception exception)
    {
        List<Exception> chain = [exception];
        for (int index = 0; index < chain.Count; index++)
        {
            Exception current = chain[index];
            if (current is HttpRequestException { StatusCode: > 0 and var status })
            {
                return (int)status;
            }

            // HttpClient words a non-success as "Response status code does not indicate success:
            // 401 (Unauthorized)." The wording is culture-independent, so matching it textually is
            // stricter than a loose digit search that a URL or version could satisfy.
            Group refused = Regex
                .Match(
                    current.Message,
                    @"does not indicate success: (40[13]) \((Unauthorized|Forbidden)\)"
                )
                .Groups[1];
            if (
                refused.Success
                && int.TryParse(refused.Value, CultureInfo.InvariantCulture, out int parsed)
            )
            {
                return parsed;
            }

            if (current.InnerException is { } inner)
            {
                chain.Add(inner);
            }

            if (current is AggregateException { InnerExceptions.Count: > 0 } aggregate)
            {
                chain.AddRange(aggregate.InnerExceptions);
            }
        }

        return null;
    }

    /// <summary>
    /// One feed call, with the one case the seam recognises: a refusal the client's chain names as
    /// 401 or 403 becomes the authentication diagnosis for the feed it was refused by. Everything
    /// else propagates unchanged.
    /// </summary>
    private async Task<T> GuardedAsync<T>(PackageSource source, Func<Task<T>> feedCall)
    {
        try
        {
            return await feedCall().ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException && IsAuthenticationFailure(exception))
        {
            throw AuthenticationFailureFor(source, exception);
        }
    }

    /// <summary>The diagnosis for a refused request against one configured feed.</summary>
    private FeedAuthenticationException AuthenticationFailureFor(
        PackageSource source,
        Exception exception
    )
    {
        int? status = StatusOf(exception);
        string name = source.Name ?? source.Source;
        List<string> files = SettingsFiles();
        // GetConfigRoots answers the roots of the merged settings chain — a root is the directory
        // holding its nuget.config, which is what a user edits.
        string rooted =
            files.Count > 0
                ? "the nuget.config chain rooted at " + string.Join(", ", files)
                : "the nuget.config that defines the source";
        return new FeedAuthenticationException(
            FormattableString.Invariant($"authentication failed for feed '{name}' ({status}); ")
                + "the host does not prompt for credentials. Add <credentials> for the "
                + $"'{name}' source under the <packageSourceCredentials> section of {rooted}, or "
                + "provide the feed's credentials with a NuGet authentication plugin.",
            exception
        );
    }

    /// <summary>
    /// The configuration files the source settings were read from, so the diagnosis names a file a
    /// user can actually edit, rather than a guess about where the chain merges.
    /// </summary>
    private List<string> SettingsFiles()
    {
        try
        {
            ISettings settings = Settings.LoadDefaultSettings(_settingsRoot ?? ".");
            return [.. settings.GetConfigRoots()];
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return [];
        }
    }
}

/// <summary>
/// The one failure a refused credential-protected feed produces. A subtype of <see
/// cref="InvalidOperationException"/> so callers that refuse installs stay unchanged — only the
/// message is new.
/// </summary>
internal sealed class FeedAuthenticationException(string message, Exception inner)
    : InvalidOperationException(message, inner) { }
