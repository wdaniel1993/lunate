using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

internal sealed class NullAgentEvents : IAgentEvents
{
    public void Emit(AgentEvent agentEvent) { }
}

internal static class Changes
{
    internal static FileChangeEvent Event(
        string absolutePath,
        string? relativePath = null,
        string workspaceId = "/work",
        string? runId = null,
        FileChangeKind kind = FileChangeKind.Modified
    ) =>
        new(absolutePath, relativePath ?? Path.GetFileName(absolutePath), workspaceId, runId, kind);
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
