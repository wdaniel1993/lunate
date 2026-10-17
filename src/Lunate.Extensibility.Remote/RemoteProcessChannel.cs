using System.Globalization;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Remote;

/// <summary>
/// The production <see cref="IRemoteLineChannel"/>: one interactive child started through the core
/// process capability (<see cref="IProcessRunner.StartSessionAsync"/>), stdout framed as NDJSON
/// and stderr pumped into the extension log, capped. The session's tree kill (Job Object on
/// Windows, children-first POSIX walk) stays the only kill path.
/// </summary>
public sealed class RemoteProcessChannel : IRemoteLineChannel
{
    private readonly IProcessSession _session;
    private readonly Task _stderrPump;

    private RemoteProcessChannel(
        IProcessSession session,
        string extensionId,
        IExtensionLog log,
        int maxLogBytes
    )
    {
        _session = session;
        _stderrPump = PumpStderrAsync(session, extensionId, log, maxLogBytes);
    }

    /// <summary>The underlying interactive process session (exit observation, shutdown grace).</summary>
    public IProcessSession Session => _session;

    /// <summary>Starts the extension's process with the session request.</summary>
    public static async Task<RemoteProcessChannel> StartAsync(
        IProcessRunner runner,
        ProcessSessionRequest request,
        string extensionId,
        IExtensionLog log,
        int maxLogBytes = 64 * 1024,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(log);

        IProcessSession session = await runner
            .StartSessionAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return new RemoteProcessChannel(session, extensionId, log, maxLogBytes);
    }

    /// <summary>
    /// Reads one protocol line; the process session's oversized-line rejection (the 16 MB cap) is
    /// surfaced as the protocol error it is.
    /// </summary>
    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _session.ReadOutputLineAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            throw new RemoteProtocolException(exception.Message, exception);
        }
    }

    public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        try
        {
            await _session.WriteInputLineAsync(line, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            throw new RemoteChannelClosedException(exception.Message, exception);
        }
    }

    /// <summary>
    /// Sends a shutdown-agnostic close: the caller owns the grace and the kill through
    /// <see cref="Session"/> first; disposal kills a still-running child as the fail-safe.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _session.DisposeAsync().ConfigureAwait(false);
        try
        {
            await _stderrPump.ConfigureAwait(false);
        }
        catch (Exception) { }
    }

    private static async Task PumpStderrAsync(
        IProcessSession session,
        string extensionId,
        IExtensionLog log,
        int maxLogBytes
    )
    {
        var logged = 0;
        var capped = false;
        while (true)
        {
            string? line;
            try
            {
                line = await session
                    .ReadErrorLineAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                log.Warn($"extension '{extensionId}': an oversized stderr line was discarded.");
                continue;
            }
            catch (Exception)
            {
                return;
            }

            if (line is null)
            {
                return;
            }

            if (capped)
            {
                continue;
            }

            int size = line.Length + 1;
            if (logged + size > maxLogBytes)
            {
                capped = true;
                log.Warn(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"extension '{extensionId}': stderr output reached the {maxLogBytes}-byte cap; the rest was discarded."
                    )
                );
                continue;
            }

            logged += size;
            log.Info($"extension '{extensionId}': {line}");
        }
    }
}
