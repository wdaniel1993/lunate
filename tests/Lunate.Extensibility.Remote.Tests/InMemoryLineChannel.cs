using System.Threading.Channels;
using Lunate.Extensibility.Remote;

namespace Lunate.Extensibility.Remote.Tests;

/// <summary>
/// One in-memory NDJSON channel: written lines surface through the peer's read queue; a completed
/// channel reads as end of stream (null). The crossed pair wires two ends' writes into each
/// other's reads.
/// </summary>
internal sealed class InMemoryLineChannel : IRemoteLineChannel
{
    private readonly Channel<string> _incoming = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
    );

    public static (InMemoryLineChannel A, InMemoryLineChannel B) Pair()
    {
        var first = new InMemoryLineChannel();
        var second = new InMemoryLineChannel();
        first.Peer = second;
        second.Peer = first;
        return (first, second);
    }

    private InMemoryLineChannel? Peer { get; set; }

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _incoming.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        if (Peer is not { } peer)
        {
            return;
        }

        try
        {
            await peer._incoming.Writer.WriteAsync(line, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            // The peer stopped reading; the wire's close path handles it.
        }
    }

    public ValueTask DisposeAsync()
    {
        _incoming.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
