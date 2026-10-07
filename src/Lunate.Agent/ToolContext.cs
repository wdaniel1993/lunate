using System.Text.Json;

namespace Lunate.Agent;

/// <summary>What a tool knows about the run it executes in.</summary>
public sealed record ToolContext(string WorkingDirectory, IAgentEvents Events)
{
    /// <summary>The run the tool executes in; null when the tool runs outside the loop.</summary>
    public string? RunId { get; init; }

    /// <summary>The call id of this execution; nested ids read <c>"&lt;parent&gt;/&lt;n&gt;"</c>.</summary>
    public string? CallId { get; init; }

    /// <summary>
    /// Runs a nested tool call through the same pipeline (validation, approval, cancellation and
    /// events); null when the tool runs outside the loop.
    /// </summary>
    public Func<
        string,
        JsonElement,
        CancellationToken,
        Task<ToolResult>
    >? ExecuteToolAsync { get; init; }

    /// <summary>Reports progress to the run's event stream as a <see cref="ToolProgressUpdate"/>.</summary>
    public Action<string>? Progress { get; init; }

    /// <summary>The queue serializing file mutations per path.</summary>
    public IFileMutationQueue FileMutations { get; init; } = FileMutationQueue.Shared;
}
