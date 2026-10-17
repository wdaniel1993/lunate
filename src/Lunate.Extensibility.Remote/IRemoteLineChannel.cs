namespace Lunate.Extensibility.Remote;

/// <summary>
/// One NDJSON duplex channel: lines in, lines out. The production channel wraps an
/// <see cref="Abstractions.IProcessSession"/>; tests use in-memory pairs. A null read means the
/// peer's output ended.
/// </summary>
public interface IRemoteLineChannel : IAsyncDisposable
{
    /// <summary>Reads one line; null at end of stream.</summary>
    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken);

    /// <summary>Writes one line (the newline is framing, added by the channel).</summary>
    ValueTask WriteLineAsync(string line, CancellationToken cancellationToken);
}
