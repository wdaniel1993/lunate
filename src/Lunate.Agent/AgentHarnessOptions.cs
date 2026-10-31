using Lunate.Ai;
using Lunate.Contracts;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>Configuration for an <see cref="AgentHarness"/>.</summary>
public sealed record AgentHarnessOptions
{
    /// <summary>The usage identity of this harness's primary model calls; defaults to <c>chat</c>.</summary>
    public string Usage { get; init; } = "chat";

    /// <summary>The resolved client/profile/provider for each configured model usage.</summary>
    public IReadOnlyDictionary<string, UsageModelClient>? UsageModels { get; init; }

    /// <summary>Whether the configured decision profile observes or can veto approval asks.</summary>
    public DecisionMode DecisionMode { get; init; } = DecisionMode.Off;

    /// <summary>The maximum number of model calls per run.</summary>
    public int MaxSteps { get; init; } = 50;

    /// <summary>The maximum number of retries per model call after a transient failure.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>The delay before the first retry; doubles per further retry.</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The system prompt sent before the history; null sends no system message.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>
    /// Extra prompt sections appended after <see cref="SystemPrompt"/>, in order (for example the
    /// session-start skills index). Appending keeps the system prefix cache-stable; the sections
    /// count toward compaction like the base prompt.
    /// </summary>
    public IReadOnlyList<AgentPromptSection>? PromptSections { get; init; }

    /// <summary>The directory tools run in.</summary>
    public string WorkingDirectory { get; init; } = Environment.CurrentDirectory;

    /// <summary>
    /// Builds the tool set of a child run that names another workspace root. Without it such a
    /// spawn is refused with a teaching error (<c>workspace_tools_unavailable</c>): the child must
    /// never run with the parent's tools in the wrong directory. Null keeps the inherit path
    /// unchanged.
    /// </summary>
    public Func<string, ToolRegistry>? WorkspaceTools { get; init; }

    /// <summary>The approval seam; null allows every tool call.</summary>
    public IToolApprover? Approver { get; init; }

    /// <summary>The extension seam; null runs without hooks and behaves as before.</summary>
    public IAgentHookPoints? Hooks { get; init; }

    /// <summary>The session to resume from and mirror into; null keeps the history in memory only.</summary>
    public Session? Session { get; init; }

    /// <summary>The run's interactive question handler; null when no user-input mechanism is present.</summary>
    internal Func<
        IReadOnlyList<UserQuestion>,
        CancellationToken,
        Task<UserQuestionResult>
    >? AskUserAsync { get; init; }

    /// <summary>Whether the interactive question picker may show decision-profile suggestions.</summary>
    internal bool QuestionSuggestionsEnabled { get; init; }

    /// <summary>The host's <c>IDecisionService</c> bridge; null when no configured profile is available.</summary>
    internal Func<
        DecisionRequest,
        CancellationToken,
        Task<DecisionResponse>
    >? QuestionDecisionAsync { get; init; }

    /// <summary>The steering queue the loop drains before each model request; null disables steering.</summary>
    public SteeringQueue? Steering { get; init; }

    /// <summary>The maximum nested tool call depth; deeper calls become error results.</summary>
    public int MaxNestedToolDepth { get; init; } = 5;

    /// <summary>The character budget for one tool result before truncation; the resolved setting threads here.</summary>
    public int ToolOutputLimit { get; init; } = ToolOutput.DefaultLimit;

    /// <summary>The estimated-token threshold above which a tool result is spilled.</summary>
    public int SpillThresholdTokens { get; init; } = ToolOutput.DefaultSpillThresholdTokens;

    /// <summary>
    /// The exact-value mask applied to a tool result's model-facing output at admission, before
    /// truncation, so a value that crosses a cut cannot send a fragment. The masked text is what
    /// enters the emitted <c>ToolCallResult</c>, the history, the session entry and the spill
    /// file; null is the identity. The host supplies it for session and no-session runs alike,
    /// standing in for the redactor it built, and it must be idempotent with the redaction at rest.
    /// </summary>
    public Func<string, string>? ToolOutputMask { get; init; }

    /// <summary>The file mutation queue shared with tools through <see cref="ToolContext"/>.</summary>
    public IFileMutationQueue FileMutations { get; init; } = FileMutationQueue.Shared;

    /// <summary>The file change sink the host injects into file-mutating tools; no-op by default.</summary>
    public IFileChangeSink FileChanges { get; init; } = NullFileChangeSink.Instance;

    /// <summary>
    /// The active model of the run, stamped into <see cref="ToolContext.Model"/> for tools; null
    /// when unknown. Tools must treat an unknown model as having no capabilities (the conservative
    /// default), never as having every capability.
    /// </summary>
    public ModelInfo? Model { get; init; }

    /// <summary>The model catalog the compaction context-window lookup uses; null skips the lookup.</summary>
    public ModelCatalog? ModelCatalog { get; init; }

    /// <summary>
    /// The run's model registry: it backs <see cref="ToolContext.Models"/> and attributes routed
    /// calls; null leaves tools without model access.
    /// </summary>
    public IModelRegistry? ModelRegistry { get; init; }

    /// <summary>The model id the compaction context-window lookup uses; null skips the lookup.</summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// The session thinking level: how much reasoning the model should do. <see cref="ThinkingLevel.Off"/>
    /// (the default) sends nothing; an on level is translated per the model's declared reasoning style
    /// where the request options are composed.
    /// </summary>
    public ThinkingLevel ThinkingLevel { get; init; } = ThinkingLevel.Off;

    /// <summary>The number of most recent turns compaction keeps verbatim.</summary>
    public int CompactionKeepTurns { get; init; } = CompactionReducer.DefaultKeepTurns;

    /// <summary>The number of model turns between request-time projection rewrites.</summary>
    public int ProjectionBatchTurns { get; init; } = 4;

    /// <summary>
    /// The cumulative input+output token budget of this run, checked before each model call; null
    /// means unlimited. The subagent runner sets the child run's budget to the tighter of the
    /// per-child and the tree's remaining budget.
    /// </summary>
    public long? TokenBudget { get; init; }

    /// <summary>The resolved subagent limits of the run tree; the runner threads the tree state down.</summary>
    public SubagentLimits SubagentLimits { get; init; } = new();
}

/// <summary>The host-resolved chat client and attribution identity for one model usage.</summary>
public sealed record UsageModelClient(
    IChatClient Client,
    string ProfileName,
    string ProviderName,
    ModelInfo Model,
    double? DecisionThreshold = null
)
{
    /// <summary>The provider template kind of <see cref="Model"/>.</summary>
    public string ProviderKind => Model.Provider;
}
