using System.Text.Json;
using Lunate.Ai;

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

    /// <summary>
    /// The active model of the run; null when the tool runs outside a run or the model is unknown.
    /// It is stamped in by the harness and never resolved from a catalog here: null stays null, and
    /// tools must treat an unknown model as having no capabilities (the conservative default).
    /// </summary>
    public ModelInfo? Model { get; init; }

    /// <summary>
    /// The stubs of the run's programmatically callable tools (<see cref="ToolExposure.Direct"/>,
    /// <see cref="ToolExposure.Programmatic"/> and <see cref="ToolExposure.Deferred"/>), in
    /// registration order; the harness stamps them in. Null when the tool runs outside the loop.
    /// </summary>
    public IReadOnlyList<ToolStub>? ToolStubs { get; init; }

    /// <summary>
    /// Run-bound model access: every nested call through it charges this run's usage and reports
    /// its routing. Null outside a run or when the run has no model registry.
    /// </summary>
    public IModelAccess? Models { get; init; }

    /// <summary>Opaque Agent-owned execution state; Contracts does not depend on the agent.</summary>
    internal object? AgentState { get; init; }

}
