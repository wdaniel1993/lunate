using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility;

namespace Lunate.Extensibility.Testing;

/// <summary>Options for an <see cref="ExtensionTestHost"/>.</summary>
public sealed record ExtensionTestHostOptions
{
    /// <summary>
    /// The client factory the model registry uses for catalog models and declared providers; null
    /// builds the real <see cref="ChatClientFactory"/> (declared providers then reach their
    /// declared endpoints with the host-read secret).
    /// </summary>
    public IChatClientFactory? ClientFactory { get; init; }

    /// <summary>
    /// The caller-owned temp directory every piece of host state lives under. The host creates its
    /// worktree, store and session inside it and never touches the real <c>~/.lunate</c>.
    /// </summary>
    public required string TempDirectory { get; init; }

    /// <summary>The manifest id of the extension under test.</summary>
    public required string ExtensionId { get; init; }

    /// <summary>An existing directory whose files are copied into the temp extension directory.</summary>
    public string? ExtensionDirectory { get; init; }

    /// <summary>Text files (relative path to contents) written into the temp extension directory.</summary>
    public IReadOnlyDictionary<string, string>? SourceFiles { get; init; }

    /// <summary>The queued trust decisions; defaults to one allow.</summary>
    public IReadOnlyList<bool>? TrustDecisions { get; init; }

    /// <summary>The tool approver handed to the harness; null allows every call.</summary>
    public IToolApprover? Approver { get; init; }

    /// <summary>The system prompt sent before the history; null sends none.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>The repository identity the extension is trusted for.</summary>
    public string RepositoryIdentity { get; init; } = "test-repository";

    /// <summary>The maximum number of model calls per run.</summary>
    public int MaxSteps { get; init; } = 8;

    /// <summary>The subagent limits of the run tree; null uses the defaults.</summary>
    public SubagentLimits? SubagentLimits { get; init; }

    /// <summary>Adjusts the composed harness options; tests replace hooks or pin budgets here.</summary>
    public Func<AgentHarnessOptions, AgentHarnessOptions>? ConfigureHarness { get; init; }

    /// <summary>The hook runner options; null uses the runner's defaults.</summary>
    public HookRunnerOptions? HookRunnerOptions { get; init; }

    /// <summary>
    /// The built-in extensions the host folds into discovery before the store scan, in composition
    /// order; empty by default. The host still loads and exposes only <see cref="ExtensionId"/> —
    /// call the loader for the built-ins a test needs.
    /// </summary>
    public IReadOnlyList<BuiltInExtension>? BuiltIns { get; init; }
}
