using System.Threading.Channels;

namespace Lunate.Agent;

/// <summary>
/// The harness boundary: emits are ordered and non-blocking, and <see cref="Complete"/> ends the stream.
/// Unbounded by design: a run's event volume is bounded by the loop's structure (steps and streamed
/// fragments) and the harness reads continuously, while a bounded channel plus the synchronous
/// <see cref="IAgentEvents.Emit"/> would need a sync-over-async wait that risks deadlocking.
/// </summary>
internal sealed class AgentEventChannel(string? sessionId = null) : IAgentEvents
{
    private readonly Channel<AgentEvent> _channel = Channel.CreateUnbounded<AgentEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
    );

    public void Emit(AgentEvent agentEvent) =>
        _channel.Writer.TryWrite(
            sessionId is not null && agentEvent.SessionId is null
                ? agentEvent with
                {
                    SessionId = sessionId,
                }
                : agentEvent
        );

    public void Complete() => _channel.Writer.TryComplete();

    public IAsyncEnumerable<AgentEvent> ReadAllAsync(
        CancellationToken cancellationToken = default
    ) => _channel.Reader.ReadAllAsync(cancellationToken);
}
