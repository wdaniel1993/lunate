using System.Threading.Channels;

namespace Lunate.Agent;

/// <summary>
/// The harness boundary: emits are ordered and non-blocking, and <see cref="Complete"/> ends the stream.
/// Unbounded by design: a run's event volume is bounded by the loop's structure (steps and streamed
/// fragments) and the harness reads continuously, while a bounded channel plus the synchronous
/// <see cref="IAgentEvents.Emit"/> would need a sync-over-async wait that risks deadlocking once T-09
/// drives emission from the loop.
/// </summary>
internal sealed class AgentEventChannel : IAgentEvents
{
    private readonly Channel<AgentEvent> _channel = Channel.CreateUnbounded<AgentEvent>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public void Emit(AgentEvent agentEvent) => _channel.Writer.TryWrite(agentEvent);

    public void Complete() => _channel.Writer.TryComplete();

    public IAsyncEnumerable<AgentEvent> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
