using System.Diagnostics;
using System.Globalization;
using Lunate.Extensibility.Abstractions;

namespace LspDiagnosticsExtension;

/// <summary>
/// Owns the session's language server process. The process starts lazily on the first diagnostics
/// request and lives until the session ends; <see cref="StopAsync"/> is idempotent (shutdown →
/// exit → kill fallback → dispose). A dead or unresponsive process is stopped and the next request
/// starts a fresh one. Every round trip is bounded by the configured timeout; a failure is the
/// caller's to report and never throws into the run.
/// </summary>
public sealed class LspServerService : IBackgroundService
{
    private readonly LspSettings _settings;
    private readonly IExtensionLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Session? _session;

    public LspServerService(LspSettings settings, IExtensionLog log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);
        _settings = settings;
        _log = log;
    }

    /// <summary>Lazy by design: the process starts on the first diagnostics request.</summary>
    public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>Stops and disposes the process; later calls and calls before a start do nothing.</summary>
    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopSessionAsync(cancellationToken, graceful: true).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Requests diagnostics for one document, restarting a dead server if needed.</summary>
    public async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(
        string absolutePath,
        string text,
        string worktreeRoot,
        CancellationToken ct
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreeRoot);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Session session = await EnsureSessionAsync(worktreeRoot, ct).ConfigureAwait(false);
            using var roundTrip = CancellationTokenSource.CreateLinkedTokenSource(ct);
            roundTrip.CancelAfter(_settings.Timeout);
            try
            {
                return await session
                    .Client.GetDiagnosticsAsync(absolutePath, text, roundTrip.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (roundTrip.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                await StopSessionAsync(CancellationToken.None, graceful: false)
                    .ConfigureAwait(false);
                throw TimedOut();
            }
            catch
            {
                await StopSessionAsync(CancellationToken.None, graceful: false)
                    .ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Session> EnsureSessionAsync(string worktreeRoot, CancellationToken ct)
    {
        if (_session is { } existing && !existing.HasExited())
        {
            return existing;
        }

        await StopSessionAsync(CancellationToken.None, graceful: false).ConfigureAwait(false);

        Process process = StartProcess(worktreeRoot);
        var client = new LspClient(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream
        );
        client.Start();
        using var initialize = CancellationTokenSource.CreateLinkedTokenSource(ct);
        initialize.CancelAfter(_settings.Timeout);
        try
        {
            await client.InitializeAsync(worktreeRoot, initialize.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DisposeSessionAsync(new Session(process, client), graceful: false)
                .ConfigureAwait(false);
            if (
                exception is OperationCanceledException
                && initialize.IsCancellationRequested
                && !ct.IsCancellationRequested
            )
            {
                throw TimedOut();
            }

            throw;
        }

        _session = new Session(process, client);
        _log.Info($"lsp-diagnostics: started '{_settings.Command}'.");
        return _session;
    }

    private Process StartProcess(string worktreeRoot)
    {
        var startInfo = new ProcessStartInfo(_settings.Command)
        {
            WorkingDirectory = worktreeRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in _settings.Args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo };
        process.Start();
        _ = DrainAsync(process.StandardError);
        return process;
    }

    /// <summary>Reads the server's stderr so a chatty server cannot block on a full pipe.</summary>
    private static async Task DrainAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is not null) { }
        }
        catch (Exception) { }
    }

    private async Task StopSessionAsync(CancellationToken ct, bool graceful)
    {
        if (_session is not { } session)
        {
            return;
        }

        _session = null;
        await DisposeSessionAsync(session, graceful, ct).ConfigureAwait(false);
        _log.Info("lsp-diagnostics: stopped the language server.");
    }

    /// <summary>
    /// Ends one process. A graceful stop asks for shutdown first; a failed or dead session is
    /// killed directly so an unresponsive server cannot stall the handler.
    /// </summary>
    private static async Task DisposeSessionAsync(
        Session session,
        bool graceful,
        CancellationToken ct = default
    )
    {
        if (graceful)
        {
            using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(ct);
            shutdown.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await session.Client.ShutdownAsync(shutdown.Token).ConfigureAwait(false);
            }
            catch (Exception) { }
        }

        try
        {
            if (!session.Process.WaitForExit(2000))
            {
                if (OperatingSystem.IsWindows())
                {
                    session.Process.Kill(entireProcessTree: true);
                }
                else
                {
                    // The framework's tree kill SIGSTOPs the whole tree on POSIX and has wedged
                    // macOS CI VMs; the language server is a single process there.
                    session.Process.Kill();
                }

                session.Process.WaitForExit(2000);
            }
        }
        catch (Exception) { }

        try
        {
            await session.Client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception) { }

        session.Process.Dispose();
    }

    private TimeoutException TimedOut() =>
        new(
            string.Create(
                CultureInfo.InvariantCulture,
                $"the language server did not answer within {_settings.Timeout.TotalMilliseconds:0} ms"
            )
        );

    private sealed class Session(Process process, LspClient client)
    {
        public Process Process { get; } = process;

        public LspClient Client { get; } = client;

        public bool HasExited()
        {
            try
            {
                return Process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }
}
