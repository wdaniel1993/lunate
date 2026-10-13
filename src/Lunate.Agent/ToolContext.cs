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

    /// <summary>
    /// The sink a tool publishes successful file mutations through; the harness stamps the run's
    /// workspace id and run id into it. No-op outside a run.
    /// </summary>
    public IFileChangeSink FileChanges { get; init; } = NullFileChangeSink.Instance;

    /// <summary>
    /// The per-run subagent seam; the harness stamps the run's runner in. Null when the tool runs
    /// outside the loop.
    /// </summary>
    public ISubagentRunner? Subagents { get; init; }
}
