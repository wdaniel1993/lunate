using System.Threading.Channels;

namespace Lunate.Agent;

/// <summary>
/// The harness boundary: emits are ordered and bounded, and <see cref="Complete"/> ends the stream.
/// </summary>
internal sealed class AgentEventChannel : IAgentEvents
{
    private const int Capacity = 256;

    private readonly Channel<AgentEvent> _channel = Channel.CreateBounded<AgentEvent>(
        new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

    public void Emit(AgentEvent agentEvent)
    {
        if (_channel.Writer.TryWrite(agentEvent))
        {
            return;
        }

        _channel.Writer.WriteAsync(agentEvent).AsTask().GetAwaiter().GetResult();
    }

    public void Complete() => _channel.Writer.TryComplete();

    public IAsyncEnumerable<AgentEvent> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
