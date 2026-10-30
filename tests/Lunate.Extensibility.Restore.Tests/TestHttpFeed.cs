using System.Globalization;
using System.Net;
using System.Text;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// A local HTTP feed the engine reads through NuGet's own V3 client — the shape a private feed
/// (Azure Artifacts, an internal Nexus) looks like, where a flat <see cref="TestPackageFeed"/> is a
/// file path. With credentials it answers one package's flat-container conversation: the service
/// index, the version list and the nupkg. Without them, or with wrong credentials, it answers 401
/// — what a credential-protected feed does to an anonymous or mis-credentialed client — as close
/// to issue #244's reported condition as a test can sit without a live API.
/// </summary>
internal sealed class TestHttpFeed : IDisposable
{
    private const string TestPassword = "unit-test-password";

    private readonly string _lowerId;
    private readonly string? _expectedAuthorization;
    private readonly string? _lowerVersion;
    private readonly byte[]? _nupkg;
    private readonly int _port;
    private readonly HttpListener _listener;

    private TestHttpFeed(string id, string? username, string? password, int port, string? version)
    {
        _lowerId = id.ToLowerInvariant();
        _port = port;
        _expectedAuthorization =
            username is null || password is null
                ? null
                : "Basic "
                    + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        _lowerVersion = version is null
            ? null
            : NuGetVersion.Parse(version).ToNormalizedString().ToLowerInvariant();
        if (_lowerVersion is not null)
        {
            var package = new PackageBuilder
            {
                Id = id,
                Version = NuGetVersion.Parse(version!),
                Description = id,
            };
            package.Authors.Add("lunate-tests");
            string source = Path.Combine(
                Path.GetTempPath(),
                string.Concat("lunate-http-feed-", Guid.NewGuid().ToString("N"), ".bin")
            );
            File.WriteAllText(source, $"lib bytes of {id} for the Lunate restore tests");
            package.Files.Add(
                new PhysicalPackageFile
                {
                    SourcePath = source,
                    TargetPath = $"lib/net10.0/{id}.dll",
                }
            );
            MemoryStream stream = new();
            package.Save(stream);
            _nupkg = stream.ToArray();
        }

        _listener = new HttpListener();
        _listener.Prefixes.Add(FormattableString.Invariant($"http://127.0.0.1:{port}/"));
        _listener.Start();
        Url = FormattableString.Invariant($"http://127.0.0.1:{_port}/v3/index.json");
        Listen();
    }

    /// <summary>The package source URL a nuget.config points at this feed.</summary>
    internal string Url { get; }

    public void Dispose()
    {
        _listener.Stop();
        GC.SuppressFinalize(this);
    }

    /// <summary>A feed that refuses every request with 401: it wants credentials that are absent.</summary>
    internal static TestHttpFeed Refusing(string id) =>
        new(id, username: null, password: null, FreePort(), version: null);

    /// <summary>
    /// A feed that serves <paramref name="id"/>'s versions and nupkg to a request carrying exactly
    /// these basic credentials, and 401 to everyone else.
    /// </summary>
    internal static TestHttpFeed Accepting(string id, string version, string username) =>
        new(id, username, TestPassword, FreePort(), version);

    private static int FreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private void Listen()
    {
        // NuGet's client may open several connections, but the test awaits one response at a
        // time, so one request per turn is enough; the loop ends when the test stops the listener.
        Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                try
                {
                    HttpListenerContext context = await _listener
                        .GetContextAsync()
                        .ConfigureAwait(false);
                    Answer(context);
                }
                catch (Exception exception)
                    when (exception
                            is HttpListenerException
                                or ObjectDisposedException
                                or InvalidOperationException
                    )
                {
                    if (!_listener.IsListening)
                    {
                        return;
                    }
                }
            }
        });
    }

    private void Answer(HttpListenerContext context)
    {
        if (!Authorized(context))
        {
            context.Response.Headers["WWW-Authenticate"] = "Basic realm=\"test\"";
            Write(context, 401, "credentials required\n");
            return;
        }

        string path = context.Request.Url?.AbsolutePath ?? "/";
        if (path.EndsWith("/v3/index.json", StringComparison.Ordinal))
        {
            string @base = FormattableString.Invariant($"http://127.0.0.1:{_port}/");
            Write(
                context,
                200,
                // Only the flat-container resource: a FindPackageByIdResource reads versions and
                // nupkgs there, and nothing else on this feed is exercised.
                FormattableString.Invariant(
                    $$"""
                    {"version":"3.0.0","resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"{{@base}}"}]}
                    """
                ) + "\n"
            );
        }
        else if (path.EndsWith($"/{_lowerId}/index.json", StringComparison.Ordinal))
        {
            Write(
                context,
                200,
                FormattableString.Invariant($"{{\"versions\":[\"{_lowerVersion}\"]}}") + "\n"
            );
        }
        else if (
            path.EndsWith(
                $"/{_lowerId}/{_lowerVersion}/{_lowerId}.{_lowerVersion}.nupkg",
                StringComparison.Ordinal
            )
        )
        {
            Write(context, 200, _nupkg!);
        }
        else
        {
            Write(context, 404, "not found\n");
        }
    }

    private bool Authorized(HttpListenerContext context)
    {
        if (_expectedAuthorization is null)
        {
            return false;
        }

        string? header = context.Request.Headers["Authorization"];
        return string.Equals(header?.Trim(), _expectedAuthorization, StringComparison.Ordinal);
    }

    private void Write(HttpListenerContext context, int status, string body)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        byte[] payload = Encoding.UTF8.GetBytes(body);
        context.Response.ContentLength64 = payload.Length;
        context.Response.OutputStream.Write(payload);
        context.Response.OutputStream.Close();
    }

    private void Write(HttpListenerContext context, int status, byte[] body)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/octet-stream";
        context.Response.ContentLength64 = body.Length;
        context.Response.OutputStream.Write(body);
        context.Response.OutputStream.Close();
    }
}
