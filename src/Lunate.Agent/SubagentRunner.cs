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

        IReadOnlyList<string>? allowedTools = ResolveAllowList(
            request.AllowedTools,
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
            return await RunChildAsync(request, allowedTools, ct).ConfigureAwait(false);
        }
        finally
        {
            _tree.Exit();
        }
    }

    private static SubagentResult Refusal(string stopReason, string message) =>
        new(null, null, message, stopReason, IsError: true, TokensUsed: 0);

    private async Task<SubagentResult> RunChildAsync(
        SubagentRequest request,
        IReadOnlyList<string>? allowedTools,
        CancellationToken ct
    )
    {
        string childRunId = RunIds.Next();
        Session? parentSession = _harness.Options.Session;
        Session? childSession = parentSession is null ? null : CreateChildSession(parentSession);
        if (parentSession is not null && childSession is not null)
        {
            parentSession.AppendChildSession(childSession.SessionId, childRunId);
        }

        AgentHarness child = CreateChildHarness(request, allowedTools, childSession, childRunId);
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
                        tokensUsed += used;
                        _tree.AddTokens(used);
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
        string childRunId
    )
    {
        AgentHarnessOptions parentOptions = _harness.Options;
        AgentHarnessOptions childOptions = parentOptions with
        {
            Session = childSession,
            Steering = null,
            MaxSteps = ClampSteps(request.MaxSteps),
            TokenBudget = ResolveBudget(),
        };
        return new AgentHarness(_harness.ChatClient, BuildRegistry(allowedTools), childOptions)
        {
            ParentRunId = _parentRunId,
            SpawnDepth = Depth + 1,
            InheritedTreeState = _tree,
        };
    }

    /// <summary>The child budget: the tighter of its own budget and the tree's remaining budget.</summary>
    private long? ResolveBudget() =>
        (_tree.Limits.TokenBudget, _tree.RemainingTokens) switch
        {
            (null, null) => null,
            (null, long treeRemaining) => treeRemaining,
            (long perChild, null) => perChild,
            (long perChild, long treeRemaining) => Math.Min(perChild, treeRemaining),
        };

    /// <summary>Per-spawn step requests clamp to the resolved ceiling; they never loosen it.</summary>
    private int ClampSteps(int? requested) =>
        requested is { } value
            ? Math.Clamp(value, 1, _harness.Options.MaxSteps)
            : _harness.Options.MaxSteps;

    /// <summary>The named subset in the parent's registration order; null names the parent's full set.</summary>
    private ToolRegistry BuildRegistry(IReadOnlyList<string>? allowedTools)
    {
        var registry = new ToolRegistry();
        if (allowedTools is null)
        {
            foreach (ITool tool in _harness.Tools.Tools)
            {
                registry.Add(tool);
            }

            return registry;
        }

        HashSet<string> selected = new(allowedTools, StringComparer.Ordinal);
        foreach (ITool tool in _harness.Tools.Tools)
        {
            if (selected.Contains(tool.Name))
            {
                registry.Add(tool);
            }
        }

        return registry;
    }

    private IReadOnlyList<string>? ResolveAllowList(
        IReadOnlyList<string>? names,
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
            if (_harness.Tools.Find(name) is null)
            {
                unknown = name;
                return null;
            }
        }

        return names;
    }

    /// <summary>
    /// Creates the child session file beside the parent's (same sessions directory, fresh id) and
    /// mirrors the parent's header fields, so the worktree/repo grouping is preserved.
    /// </summary>
    private static Session CreateChildSession(Session parent)
    {
        string directory =
            Path.GetDirectoryName(Path.GetFullPath(parent.Path))
            ?? throw new InvalidOperationException(
                $"The parent session path '{parent.Path}' has no directory to place a child session in."
            );
        Directory.CreateDirectory(directory);
        string provisional = Path.Combine(directory, Path.GetRandomFileName());
        Session created = Session.Create(
            provisional,
            parent.Header.Cwd,
            parent.Header.Repo,
            parent.Header.Worktree
        );
        string path = Path.Combine(directory, SessionPaths.SessionFileName(created.SessionId));
        File.Move(provisional, path);
        return Session.Load(path);
    }
}
