using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

internal sealed class NullAgentEvents : IAgentEvents
{
    public void Emit(AgentEvent agentEvent) { }
}

internal sealed class TestFileChangedHandler(
    int priority,
    Func<FileChangedPayload, CancellationToken, ValueTask> handler
) : IFileChangedHandler
{
    public int Priority => priority;

    public ValueTask OnFileChangedAsync(
        FileChangedPayload payload,
        CancellationToken cancellationToken
    ) => handler(payload, cancellationToken);
}

internal sealed class RecordingFileChangedHandler : IFileChangedHandler
{
    public int Priority => 0;

    public List<FileChangedPayload> Payloads { get; } = [];

    public ValueTask OnFileChangedAsync(
        FileChangedPayload payload,
        CancellationToken cancellationToken
    )
    {
        Payloads.Add(payload);
        return ValueTask.CompletedTask;
    }
}
