using System.Globalization;
using System.Text;

namespace Lunate.Agent;

/// <summary>
/// The one shared state of a top-level run tree: every live child run of the tree counts against
/// <see cref="SubagentLimits.Concurrency"/>, and the tokens every child consumes count against
/// <see cref="SubagentLimits.TreeTokenBudget"/>. Children share the state; it is created at depth 0
/// and passed down (spawning deeper is refused before this version reaches it).
/// </summary>
internal sealed class SubagentTreeState(SubagentLimits limits)
{
    private readonly object _gate = new();
    private int _liveChildren;
    private long _tokensUsed;

    /// <summary>The resolved limits of the tree.</summary>
    public SubagentLimits Limits { get; } = limits;

    /// <summary>The tree's accounting as of now: child tokens consumed and live children.</summary>
    public SubagentUsage Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new SubagentUsage(_tokensUsed, _liveChildren);
            }
        }
    }

    /// <summary>The tree token budget still available; null when the tree budget is unlimited.</summary>
    public long? RemainingTokens
    {
        get
        {
            lock (_gate)
            {
                return Limits.TreeTokenBudget is { } budget ? budget - _tokensUsed : null;
            }
        }
    }

    /// <summary>
    /// Reserves one child slot; refuses immediately (never blocks) while
    /// <see cref="SubagentLimits.Concurrency"/> is held.
    /// </summary>
    public bool TryEnter(out string? refusal)
    {
        lock (_gate)
        {
            if (Limits.Concurrency is { } cap && _liveChildren >= cap)
            {
                refusal =
                    $"Subagent spawn refused (concurrency_limit): subagents.concurrency allows at most "
                    + cap.ToString(CultureInfo.InvariantCulture)
                    + $" live subagent run{(_liveChildren == 1 ? "" : "s")} per run tree and "
                    + _liveChildren.ToString(CultureInfo.InvariantCulture)
                    + " are live. Wait for a running subagent to finish and retry.";
                return false;
            }

            _liveChildren++;
            refusal = null;
            return true;
        }
    }

    /// <summary>Releases one child slot.</summary>
    public void Exit()
    {
        lock (_gate)
        {
            _liveChildren--;
        }
    }

    /// <summary>Adds the tokens one child consumed to the tree's cumulative usage.</summary>
    public void AddTokens(long tokens)
    {
        lock (_gate)
        {
            _tokensUsed += tokens;
        }
    }
}

/// <summary>
/// Runs one child harness per call from the parent's live facts and forwards the child's events
/// onto the parent's channel. Constructed per run by the harness and stamped into
/// <see cref="ToolContext.Subagents"/>; <see cref="ISubagentRunner.Depth"/> is the run's own spawn
/// depth. Depth is fixed at 1 in this version: a depth-1 runner refuses every spawn.
/// </summary>
internal sealed class SubagentRunner : ISubagentRunner
{
    /// <summary>The fixed maximum spawn depth of this version.</summary>
    internal const int MaxDepth = 1;

    private readonly AgentHarness _harness;
    private readonly AgentEventChannel _channel;
    private readonly string _parentRunId;
    private readonly SubagentTreeState _tree;

    internal SubagentRunner(
        AgentHarness harness,
        AgentEventChannel channel,
        string parentRunId,
        SubagentTreeState tree,
        int depth
    )
    {
        _harness = harness;
        _channel = channel;
        _parentRunId = parentRunId;
        _tree = tree;
        Depth = depth;
    }

    public int Depth { get; }

    public async Task<SubagentResult> RunAsync(SubagentRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);

        if (Depth >= MaxDepth)
        {
            return Refusal(
                StopReasons.DepthLimit,
                "Subagent spawn refused (depth_limit): the subagent depth limit is fixed at 1 in "
                    + "this version (not configurable), so a subagent cannot spawn another subagent."
            );
        }

        ChildWorkspace? workspace = ResolveWorkspace(request, out SubagentResult? workspaceRefusal);
        if (workspaceRefusal is not null)
        {
            return workspaceRefusal;
        }

        ToolRegistry? workspaceTools = null;
        if (workspace is { } target)
        {
            if (_harness.Options.WorkspaceTools is not { } factory)
            {
                return Refusal(
                    StopReasons.WorkspaceToolsUnavailable,
                    $"Subagent spawn refused (workspace_tools_unavailable): no workspace-tool "
                        + $"factory is configured, so a child run in '{target.Root}' cannot be given "
                        + "tools built for that workspace. The composer must supply "
                        + "AgentHarnessOptions.WorkspaceTools; never run a child with the parent's "
                        + "tools in the wrong directory."
                );
            }

            workspaceTools = factory(target.Root);
        }

        IReadOnlyList<string>? allowedTools = ResolveAllowList(
            request.AllowedTools,
            workspaceTools ?? _harness.Tools,
            out string? unknown
        );
        if (unknown is not null)
        {
            return Refusal(
                StopReasons.Error,
                $"Subagent spawn refused: unknown tool '{unknown}' in the allow-list. "
                    + "The allow-list may only name tools the run itself offers."
            );
        }

        if (_tree.RemainingTokens is { } remaining && remaining <= 0)
        {
            return Refusal(
                StopReasons.TokenLimit,
                "Subagent spawn refused (token_limit): the run tree has no budget left "
                    + "(subagents.treeTokenBudget). No subagent can start until a new top-level run begins."
            );
        }

        if (!_tree.TryEnter(out string? concurrencyRefusal))
        {
            return Refusal(StopReasons.ConcurrencyLimit, concurrencyRefusal!);
        }

        try
        {
            return await RunChildAsync(request, allowedTools, workspace, workspaceTools, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _tree.Exit();
        }
    }

    private static SubagentResult Refusal(string stopReason, string message) =>
        new(null, null, message, stopReason, IsError: true, TokensUsed: 0);

    /// <summary>
    /// Resolves the request's workspace root: null means the inherit path (no root named, or the
    /// parent's own root); a value means the child runs in another worktree of the parent's
    /// repository. Every refusal is a teaching error result, never an exception.
    /// </summary>
    private ChildWorkspace? ResolveWorkspace(SubagentRequest request, out SubagentResult? refusal)
    {
        refusal = null;
        string requested = request.WorkspaceRoot ?? string.Empty;
        if (string.IsNullOrWhiteSpace(requested))
        {
            return null;
        }

        string canonical;
        try
        {
            canonical = GitIdentity.Canonicalize(requested);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            refusal = Refusal(StopReasons.WorkspaceMissing, MissingMessage(requested));
            return null;
        }

        if (!Directory.Exists(canonical))
        {
            refusal = Refusal(StopReasons.WorkspaceMissing, MissingMessage(requested));
            return null;
        }

        string parentRoot = CanonicalParentRoot();
        if (PathsEqual(canonical, parentRoot))
        {
            return null;
        }

        (_, string? parentCommonDir) = GitIdentity.Detect(parentRoot);
        (_, string? childCommonDir) = GitIdentity.Detect(canonical);
        if (
            parentCommonDir is null
            || childCommonDir is null
            || !PathsEqual(parentCommonDir, childCommonDir)
        )
        {
            refusal = Refusal(
                StopReasons.WorkspaceOutsideRepository,
                $"Subagent spawn refused (workspace_outside_repository): the workspace root "
                    + $"'{canonical}' is not a worktree of the parent run's repository (it must "
                    + "share the same git-common-dir). Child runs may only target worktrees of the "
                    + "repository the parent runs in; create one with 'git worktree add' and retry."
            );
            return null;
        }

        return new ChildWorkspace(canonical, childCommonDir);
    }

    private static string MissingMessage(string requested) =>
        $"Subagent spawn refused (workspace_missing): the workspace root '{requested}' does not "
        + "exist or is not a directory. Create the worktree first (for example with 'git worktree "
        + "add') and retry, or omit the workspace root to inherit the parent's.";

    /// <summary>The parent run's canonical workspace root; best effort outside a valid path.</summary>
    private string CanonicalParentRoot()
    {
        string root = _harness.Options.WorkingDirectory;
        try
        {
            return GitIdentity.Canonicalize(root);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            return Path.GetFullPath(root);
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left,
            right,
            OperatingSystem.IsLinux()
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase
        );

    private async Task<SubagentResult> RunChildAsync(
        SubagentRequest request,
        IReadOnlyList<string>? allowedTools,
        ChildWorkspace? workspace,
        ToolRegistry? workspaceTools,
        CancellationToken ct
    )
    {
        string childRunId = RunIds.Next();
        Session? parentSession = _harness.Options.Session;
        Session? childSession = parentSession is null
            ? null
            : CreateChildSession(parentSession, workspace);
        if (parentSession is not null && childSession is not null)
        {
            parentSession.AppendChildSession(childSession.SessionId, childRunId);
        }

        AgentHarness child = CreateChildHarness(
            request,
            allowedTools,
            childSession,
            childRunId,
            workspace,
            workspaceTools
        );
        var completedText = new StringBuilder();
        var currentText = new StringBuilder();
        long tokensUsed = 0;
        string stopReason = StopReasons.Error;
        string? errorMessage = null;
        bool stopped = false;
        try
        {
            await foreach (AgentEvent agentEvent in child.RunAsync(request.Prompt, childRunId, ct))
            {
                switch (agentEvent)
                {
                    case TextMessageStart:
                        currentText.Clear();
                        break;
                    case TextMessageContent content:
                        currentText.Append(content.Text);
                        break;
                    case TextMessageEnd:
                        completedText.Clear();
                        completedText.Append(currentText);
                        currentText.Clear();
                        break;
                    case UsageUpdated usage:
                        long used =
                            (usage.Usage.InputTokenCount ?? 0)
                            + (usage.Usage.OutputTokenCount ?? 0);
                        // The tree accounting is fed by the child harness itself (synchronously);
                        // the runner only totals the child's usage for its result.
                        tokensUsed += used;
                        break;
                    case RunFinished finished:
                        stopReason = finished.StopReason;
                        stopped = true;
                        break;
                    case RunError error:
                        errorMessage = error.Message;
                        stopped = true;
                        break;
                }

                _channel.Emit(agentEvent with { ParentRunId = _parentRunId });
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            stopReason = StopReasons.Cancelled;
            stopped = true;
        }

        string output = currentText.Length > 0 ? currentText.ToString() : completedText.ToString();
        if (errorMessage is { Length: > 0 })
        {
            output = output.Length > 0 ? errorMessage + "\n\n" + output : errorMessage;
        }

        if (!stopped)
        {
            stopReason = StopReasons.Error;
        }

        bool isError = stopReason is not (StopReasons.Stop or StopReasons.Length);
        return new SubagentResult(
            childSession?.SessionId,
            childRunId,
            output,
            stopReason,
            isError,
            tokensUsed
        );
    }

    private AgentHarness CreateChildHarness(
        SubagentRequest request,
        IReadOnlyList<string>? allowedTools,
        Session? childSession,
        string childRunId,
        ChildWorkspace? workspace,
        ToolRegistry? workspaceTools
    )
    {
        AgentHarnessOptions parentOptions = _harness.Options;
        AgentHarnessOptions childOptions = parentOptions with
        {
            Session = childSession,
            Steering = null,
            MaxSteps = ClampSteps(request.MaxSteps),
            // The per-child ceiling. The tree's remaining budget is checked live by the child
            // loop before every model call, so concurrent children cannot freeze it at spawn.
            TokenBudget = _tree.Limits.TokenBudget,
            // The working directory follows the request's workspace; without a root it stays the
            // parent's, bit-for-bit.
            WorkingDirectory = workspace?.Root ?? parentOptions.WorkingDirectory,
        };
        ToolRegistry childTools = BuildRegistry(allowedTools, workspaceTools ?? _harness.Tools);
        return new AgentHarness(_harness.ChatClient, childTools, childOptions)
        {
            ParentRunId = _parentRunId,
            SpawnDepth = Depth + 1,
            InheritedTreeState = _tree,
        };
    }

    /// <summary>Per-spawn step requests clamp to the resolved ceiling; they never loosen it.</summary>
    private int ClampSteps(int? requested) =>
        requested is { } value
            ? Math.Clamp(value, 1, _harness.Options.MaxSteps)
            : _harness.Options.MaxSteps;

    /// <summary>The named subset in the source's registration order; null keeps the source's full set.</summary>
    private static ToolRegistry BuildRegistry(
        IReadOnlyList<string>? allowedTools,
        ToolRegistry source
    )
    {
        var registry = new ToolRegistry();
        if (allowedTools is null)
        {
            foreach (ITool tool in source.Tools)
            {
                registry.Add(tool);
            }

            return registry;
        }

        HashSet<string> selected = new(allowedTools, StringComparer.Ordinal);
        foreach (ITool tool in source.Tools)
        {
            if (selected.Contains(tool.Name))
            {
                registry.Add(tool);
            }
        }

        return registry;
    }

    /// <summary>The allow-list may only name tools of the child's own offered set.</summary>
    private static IReadOnlyList<string>? ResolveAllowList(
        IReadOnlyList<string>? names,
        ToolRegistry source,
        out string? unknown
    )
    {
        unknown = null;
        if (names is null)
        {
            return null;
        }

        foreach (string name in names)
        {
            if (source.Find(name) is null)
            {
                unknown = name;
                return null;
            }
        }

        return names;
    }

    /// <summary>
    /// Creates the child session file beside the parent's (same sessions directory, fresh id) and
    /// mirrors the parent's header fields, so the worktree/repo grouping is preserved. A child in
    /// another workspace root records its own repository identity and worktree instead.
    /// </summary>
    private static Session CreateChildSession(Session parent, ChildWorkspace? workspace)
    {
        string directory =
            Path.GetDirectoryName(Path.GetFullPath(parent.Path))
            ?? throw new InvalidOperationException(
                $"The parent session path '{parent.Path}' has no directory to place a child session in."
            );
        Session child = SessionProvisioning.Create(
            directory,
            provisional =>
                workspace is { } target
                    ? Session.Create(provisional, target.Root, target.GitCommonDir, target.Root)
                    : Session.Create(
                        provisional,
                        parent.Header.Cwd,
                        parent.Header.Repo,
                        parent.Header.Worktree
                    )
        );
        // A child persists what the parent persists: the same credentials are redacted in its file.
        child.Redactor = parent.Redactor;
        return child;
    }

    /// <summary>The canonical root and git-common-dir of a child running in another worktree.</summary>
    private sealed record ChildWorkspace(string Root, string? GitCommonDir);
}
