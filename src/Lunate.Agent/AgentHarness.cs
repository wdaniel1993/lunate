using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// The agent loop: a turn on an <see cref="IChatClient"/> that streams model output as events,
/// runs tools itself (ADR-0003) and keeps the history valid. Runs on one harness are sequential:
/// a second concurrent run is rejected; abandoning the stream stops the run.
/// </summary>
/// <remarks>
/// The client is expected to be the factory pipeline, which includes the stream accumulator, so
/// function calls arrive complete. A call that still carries raw argument fragments or a failed
/// assembly is never executed: it becomes an instructing error result instead.
/// </remarks>
public sealed partial class AgentHarness
{
    private readonly IChatClient _client;
    private readonly ToolRegistry _tools;
    private readonly AgentHarnessOptions _options;
    private readonly List<ChatMessage> _history = [];
    private readonly List<FunctionCallContent> _danglingCalls = [];
    private int _runActive;
    private int _nestedCallCounter;
    private SubagentTreeState? _treeState;
    private SubagentRunner? _subagentRunner;
    private long _tokenBudgetUsed;

    public AgentHarness(IChatClient client, ToolRegistry tools, AgentHarnessOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(tools);
        _client = client;
        _tools = tools;
        _options = options ?? new AgentHarnessOptions();
        ValidateOptions(_options);
        _compactor = new CompactionReducer(_client, _options.CompactionKeepTurns);
        if (_options.Session is { } session)
        {
            foreach (SessionHistoryItem item in session.HistoryItems())
            {
                _history.Add(item.Message);
                _historyEntryIds.Add(item.EntryId);
            }

            if (session.Entries.OfType<SessionCompactionEntry>().LastOrDefault() is not null)
            {
                _summaryMessage = _history[0];
                _replacedEntryIds =
                [
                    .. session.Entries.OfType<SessionCompactionEntry>().Last().Replaces,
                ];
            }

            _historyUtf8 = CompactionReducer.Utf8Length(_history);
        }
    }

    /// <summary>The parent run id when this harness runs a child (subagent) run; null otherwise.</summary>
    internal string? ParentRunId { get; init; }

    /// <summary>The run's spawn depth: 0 for a top-level run, 1 for a subagent run.</summary>
    internal int SpawnDepth { get; init; }

    /// <summary>The run tree's shared state when this harness is a child; null for a top-level harness.</summary>
    internal SubagentTreeState? InheritedTreeState { get; init; }

    /// <summary>The client the loop streams from; the subagent runner composes children with it.</summary>
    internal IChatClient ChatClient => _client;

    /// <summary>The run's tool registry; the subagent runner derives the child's registry from it.</summary>
    internal ToolRegistry Tools => _tools;

    /// <summary>The run's options; the subagent runner composes the child's options from them.</summary>
    internal AgentHarnessOptions Options => _options;

    /// <summary>
    /// The subagent accounting of the current or most recent top-level run tree: tokens consumed
    /// by its child runs and live children. Zero before any run.
    /// </summary>
    public SubagentUsage SubagentUsage => _treeState?.Snapshot ?? default;

    /// <summary>Runs the loop for one user input and streams its events; the stream completes when the run ends.</summary>
    public IAsyncEnumerable<AgentEvent> RunAsync(
        string userInput,
        CancellationToken ct = default
    ) => RunAsync(userInput, skill: null, RunIds.Next(), ct);

    /// <summary>
    /// Runs the loop for one skill invocation: the user turn carries the skill metadata into the
    /// session entry (the <c>/skill:&lt;name&gt;</c> path).
    /// </summary>
    public IAsyncEnumerable<AgentEvent> RunSkillAsync(
        string userInput,
        SessionSkillMetadata skill,
        CancellationToken ct
    ) => RunAsync(userInput, skill, RunIds.Next(), ct);

    /// <summary>Runs the loop under a caller-provided run id (the subagent runner links sessions by it).</summary>
    internal IAsyncEnumerable<AgentEvent> RunAsync(
        string userInput,
        string runId,
        CancellationToken ct = default
    ) => RunAsync(userInput, skill: null, runId, ct);

    internal async IAsyncEnumerable<AgentEvent> RunAsync(
        string userInput,
        SessionSkillMetadata? skill,
        string runId,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(userInput);
        if (Interlocked.CompareExchange(ref _runActive, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "A run is already in progress on this harness; runs are sequential."
            );
        }

        try
        {
            _treeState = InheritedTreeState ?? new SubagentTreeState(_options.SubagentLimits);
            _tokenBudgetUsed = 0;
            _danglingCalls.Clear();
            _compactionInputTokens = 0;
            _compactionOutputTokens = 0;
            Interlocked.Exchange(ref _nestedCallCounter, 0);
            var channel = new AgentEventChannel(_options.Session?.SessionId);
            _subagentRunner = new SubagentRunner(this, channel, runId, _treeState!, SpawnDepth);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task loop = RunLoopAsync(runId, userInput, skill, channel, linked.Token);
            try
            {
                await foreach (
                    AgentEvent agentEvent in channel.ReadAllAsync(CancellationToken.None)
                )
                {
                    yield return agentEvent;
                }
            }
            finally
            {
                linked.Cancel();
                await loop;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _runActive, 0);
        }
    }

    private static void ValidateOptions(AgentHarnessOptions options)
    {
        if (options.MaxSteps < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AgentHarnessOptions.MaxSteps),
                options.MaxSteps,
                "MaxSteps must be at least 1."
            );
        }

        if (options.MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AgentHarnessOptions.MaxRetries),
                options.MaxRetries,
                "MaxRetries must be zero or greater."
            );
        }

        if (options.RetryBaseDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AgentHarnessOptions.RetryBaseDelay),
                options.RetryBaseDelay,
                "RetryBaseDelay must be zero or greater."
            );
        }

        if (options.MaxNestedToolDepth < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AgentHarnessOptions.MaxNestedToolDepth),
                options.MaxNestedToolDepth,
                "MaxNestedToolDepth must be at least 1."
            );
        }

        if (options.CompactionKeepTurns < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AgentHarnessOptions.CompactionKeepTurns),
                options.CompactionKeepTurns,
                "CompactionKeepTurns must be at least 1."
            );
        }

        if (options.SubagentLimits.Concurrency is { } concurrency && concurrency < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AgentHarnessOptions.SubagentLimits),
                concurrency,
                "The subagent concurrency limit must be at least 1 or null (unlimited)."
            );
        }

        if (options.SubagentLimits.TokenBudget is { } tokenBudget && tokenBudget < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AgentHarnessOptions.SubagentLimits),
                tokenBudget,
                "The subagent token budget must be at least 1 or null (unlimited)."
            );
        }

        if (options.SubagentLimits.TreeTokenBudget is { } treeBudget && treeBudget < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AgentHarnessOptions.SubagentLimits),
                treeBudget,
                "The subagent tree token budget must be at least 1 or null (unlimited)."
            );
        }
    }

    private async Task RunLoopAsync(
        string runId,
        string userInput,
        SessionSkillMetadata? skill,
        AgentEventChannel channel,
        CancellationToken ct
    )
    {
        try
        {
            await ExecuteRunAsync(runId, userInput, skill, channel, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // ExecuteRunAsync turns user cancellation into RunFinished(cancelled); this guard
            // keeps the loop task from faulting should an OCE still escape.
        }
        finally
        {
            if (_options.Hooks is { } hooks)
            {
                await hooks.RunSettledAsync(
                    new AgentRunSettledContext(runId) { ParentRunId = ParentRunId },
                    CancellationToken.None
                );
            }

            channel.Complete();
        }
    }

    private async Task ExecuteRunAsync(
        string runId,
        string userInput,
        SessionSkillMetadata? skill,
        AgentEventChannel channel,
        CancellationToken ct
    )
    {
        using Activity? runActivity = AgentTelemetry.Source.StartActivity(
            $"{AgentTelemetry.InvokeAgentOperation} {AgentTelemetry.AgentName}",
            ActivityKind.Internal
        );
        runActivity?.SetTag(
            AgentTelemetry.OperationNameAttribute,
            AgentTelemetry.InvokeAgentOperation
        );
        runActivity?.SetTag(AgentTelemetry.AgentNameAttribute, AgentTelemetry.AgentName);

        bool runStarted = false;
        bool terminalEmitted = false;
        try
        {
            AppendHistoryMessage(new ChatMessage(ChatRole.User, userInput), skill: skill);
            channel.Emit(new RunStarted(runId));
            runStarted = true;

            List<AgentPromptSection> sections = InitialSections();
            IReadOnlyList<string>? activeTools = null;
            if (_options.Hooks is { } hookPoints)
            {
                AgentRunStartingResult started = await hookPoints.RunStartingAsync(
                    new AgentRunStartingContext(runId, [.. sections], ToolNames())
                    {
                        ParentRunId = ParentRunId,
                    },
                    ct
                );
                if (started is AgentRunStartingResult.Apply apply)
                {
                    sections = ApplySectionEdits(sections, apply.SectionEdits);
                    activeTools = apply.ActiveTools;
                }
            }

            int modelCalls = 0;
            while (true)
            {
                if (modelCalls == _options.MaxSteps)
                {
                    channel.Emit(new StepLimitReached(runId, _options.MaxSteps));
                    channel.Emit(new RunFinished(runId, StopReasons.StepLimit));
                    terminalEmitted = true;
                    return;
                }

                if (_options.TokenBudget is { } tokenBudget && _tokenBudgetUsed >= tokenBudget)
                {
                    channel.Emit(new RunFinished(runId, StopReasons.TokenLimit));
                    terminalEmitted = true;
                    return;
                }

                // A child run also stops when the whole tree's budget is exhausted — checked live,
                // so concurrent children cannot freeze the tree's remaining budget at spawn time.
                if (
                    InheritedTreeState is { RemainingTokens: { } treeRemaining }
                    && treeRemaining <= 0
                )
                {
                    channel.Emit(new RunFinished(runId, StopReasons.TokenLimit));
                    terminalEmitted = true;
                    return;
                }

                modelCalls++;
                DrainSteering(runId, channel);
                await CompactBeforeRequestAsync(runId, channel, sections, ct);
                ModelStreamResult stream = await StreamModelWithRetriesAsync(
                    runId,
                    channel,
                    sections,
                    activeTools,
                    ct
                );
                foreach (ChatMessage message in stream.Updates.ToChatResponse().Messages)
                {
                    ChatMessage persisted = message;
                    if (
                        _options.Hooks is { } completedHook
                        && message.Role == ChatRole.Assistant
                        && message.Contents.OfType<TextContent>().Any()
                    )
                    {
                        AgentMessageCompletedResult completed =
                            await completedHook.MessageCompletedAsync(
                                new AgentMessageCompletedContext(
                                    runId,
                                    message.Role.Value,
                                    MessageText(message)
                                )
                                {
                                    ParentRunId = ParentRunId,
                                },
                                ct
                            );
                        if (completed is AgentMessageCompletedResult.Replace replace)
                        {
                            persisted = ReplaceMessageText(message, replace.Text);
                        }
                    }

                    AppendHistoryMessage(persisted, stream.ModelId, stream.Usage);
                }

                List<FunctionCallContent> calls = DistinctCalls(stream.Updates);
                if (calls.Count == 0)
                {
                    if (_options.Hooks is { } turnHook)
                    {
                        AgentTurnEndedResult turn = await turnHook.TurnEndedAsync(
                            new AgentTurnEndedContext(runId) { ParentRunId = ParentRunId },
                            ct
                        );
                        foreach (AgentExtensionEntry entry in turn.Entries)
                        {
                            _options.Session?.AppendExtension(
                                $"ext/{entry.ExtensionId}/{entry.Type}",
                                entry.Payload.GetRawText()
                            );
                        }

                        if (turn.RequestContinuation)
                        {
                            continue;
                        }
                    }

                    channel.Emit(new RunFinished(runId, MappedStopReason(stream.FinishReason)));
                    terminalEmitted = true;
                    return;
                }

                _danglingCalls.Clear();
                _danglingCalls.AddRange(calls);
                foreach (FunctionCallContent call in calls)
                {
                    await ExecuteCallAsync(runId, call, channel, ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            RepairDanglingCalls(runId, cancelled: true);
            if (runStarted && !terminalEmitted)
            {
                channel.Emit(new RunFinished(runId, StopReasons.Cancelled));
            }
        }
        catch (Exception exception)
        {
            runActivity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            RepairDanglingCalls(runId, cancelled: false);
            channel.Emit(new RunError(runId, $"Run failed: {exception.Message}"));
        }
    }

    private async Task<ModelStreamResult> StreamModelAsync(
        string runId,
        AgentEventChannel channel,
        IReadOnlyList<AgentPromptSection> sections,
        IReadOnlyList<string>? activeTools,
        CancellationToken ct,
        ModelStreamAttempt attempt
    )
    {
        List<ChatResponseUpdate> updates = [];
        string? messageId = null;
        bool textOpen = false;
        string? reasoningMessageId = null;
        bool reasoningOpen = false;
        ChatFinishReason? finishReason = null;
        string? modelId = null;
        string? routedModelId = null;
        SessionUsage? usage = null;

        void CloseText()
        {
            if (textOpen)
            {
                textOpen = false;
                channel.Emit(new TextMessageEnd(runId, messageId!));
            }
        }

        void CloseReasoning()
        {
            if (reasoningOpen)
            {
                reasoningOpen = false;
                channel.Emit(new ReasoningEnd(runId, reasoningMessageId!));
            }
        }

        try
        {
            List<ChatMessage> request = BuildRequest(sections);
            long requestUtf8 = _historyUtf8 + SystemPromptUtf8(sections);
            if (_options.Hooks is { } hookPoints)
            {
                AgentContextBuildingResult built = await hookPoints.ContextBuildingAsync(
                    new AgentContextBuildingContext(runId, [.. request.Select(ToContextMessage)])
                    {
                        ParentRunId = ParentRunId,
                    },
                    ct
                );
                request = InsertAddedContext(request, built.AddedMessages);
            }

            using (
                ModelRouteObserver.Scope(
                    (requested, resolved) =>
                    {
                        routedModelId = resolved;
                        channel.Emit(new ModelRouted(runId, requested, resolved));
                    }
                )
            )
            {
                await foreach (
                    ChatResponseUpdate update in _client.GetStreamingResponseAsync(
                        request,
                        BuildOptions(activeTools),
                        ct
                    )
                )
                {
                    if (_options.Hooks is { } streamHook)
                    {
                        await streamHook.ProviderStreamEventAsync(runId, ParentRunId, update, ct);
                    }

                    updates.Add(update);
                    if (update.ModelId is { Length: > 0 } updateModelId)
                    {
                        modelId = updateModelId;
                    }

                    if (update.FinishReason is { } reason)
                    {
                        finishReason = reason;
                    }

                    foreach (AIContent content in update.Contents)
                    {
                        if (content is TextReasoningContent { Text: { Length: > 0 } reasoning })
                        {
                            CloseText();
                            if (!reasoningOpen)
                            {
                                reasoningOpen = true;
                                reasoningMessageId = RunIds.NextMessage();
                                channel.Emit(new ReasoningStart(runId, reasoningMessageId));
                            }

                            attempt.Emitted = true;
                            channel.Emit(
                                new ReasoningMessageContent(runId, reasoningMessageId!, reasoning)
                            );
                        }
                        else if (content is TextContent { Text: { Length: > 0 } text })
                        {
                            CloseReasoning();
                            if (!textOpen)
                            {
                                textOpen = true;
                                messageId = RunIds.NextMessage();
                                channel.Emit(new TextMessageStart(runId, messageId));
                            }

                            attempt.Emitted = true;
                            channel.Emit(new TextMessageContent(runId, messageId!, text));
                        }
                        else if (content is UsageContent usageContent)
                        {
                            attempt.Emitted = true;
                            AccumulateUsage(
                                runId,
                                channel,
                                usageContent.Details,
                                routedModelId ?? modelId ?? _options.ModelId
                            );
                            usage = new SessionUsage(
                                (int)(usageContent.Details.InputTokenCount ?? 0),
                                (int)(usageContent.Details.OutputTokenCount ?? 0)
                            );
                            _lastInputTokens = usage.Input;
                            _lastRequestUtf8 = requestUtf8;
                        }
                    }
                }
            }
        }
        finally
        {
            CloseReasoning();
            CloseText();
        }

        return new ModelStreamResult(
            updates,
            finishReason,
            attempt.Emitted,
            routedModelId ?? modelId,
            usage
        );
    }

    /// <summary>
    /// The one usage-accounting path, shared by the run's own model calls and nested calls through
    /// <see cref="ToolContext.Models"/>: emit <c>UsageUpdated</c> with the resolved model id,
    /// count into the run's token budget, and feed the inherited tree accounting when the run is a
    /// child. A child run feeds the tree synchronously here (not via the runner's event consumer),
    /// so the pre-call budget check can never race its own consumption.
    /// </summary>
    internal void AccumulateUsage(
        string runId,
        AgentEventChannel channel,
        UsageDetails details,
        string? modelId
    )
    {
        channel.Emit(new UsageUpdated(runId, details) { ModelId = modelId });
        long used = (details.InputTokenCount ?? 0) + (details.OutputTokenCount ?? 0);
        _tokenBudgetUsed += used;
        InheritedTreeState?.AddTokens(used);
    }

    /// <summary>
    /// Injects every queued steering message into the history, mirroring it into the session and
    /// announcing it. Called only by the top-level run, before each model request and after the
    /// whole previous batch of tool results; nested calls never drain the queue.
    /// </summary>
    private void DrainSteering(string runId, AgentEventChannel channel)
    {
        if (_options.Steering is not { } steering)
        {
            return;
        }

        while (steering.TryDequeueMessage(out SteeringMessage? message))
        {
            if (message is null)
            {
                continue;
            }

            string? entryId = AppendHistoryMessage(
                new ChatMessage(ChatRole.User, message.Text),
                skill: message.Skill
            );
            channel.Emit(new SteeringInjected(runId, entryId));
        }
    }

    /// <summary>Appends a history message and mirrors it into the session when one is attached.</summary>
    private string? AppendHistoryMessage(
        ChatMessage message,
        string? model = null,
        SessionUsage? usage = null,
        SessionSkillMetadata? skill = null
    )
    {
        _history.Add(message);
        _historyUtf8 += CompactionReducer.Utf8Length(message);
        string? entryId = null;
        if (_options.Session is { } session)
        {
            if (skill is null)
            {
                session.AppendMessage(message, model, usage);
            }
            else
            {
                session.AppendSkillMessage(message, skill, model, usage);
            }

            entryId = session.Entries[^1].Id;
        }

        _historyEntryIds.Add(entryId);
        return entryId;
    }

    private static string MappedStopReason(ChatFinishReason? finishReason) =>
        finishReason == ChatFinishReason.Length ? StopReasons.Length : StopReasons.Stop;

    private List<AgentPromptSection> InitialSections()
    {
        List<AgentPromptSection> sections = [];
        if (_options.SystemPrompt is { } prompt)
        {
            sections.Add(new AgentPromptSection("system", prompt));
        }

        if (_options.PromptSections is { } appended)
        {
            sections.AddRange(appended);
        }

        return sections;
    }

    private IReadOnlyList<string> ToolNames() => [.. _tools.Tools.Select(tool => tool.Name)];

    private static List<AgentPromptSection> ApplySectionEdits(
        List<AgentPromptSection> sections,
        IReadOnlyList<AgentPromptSectionEdit> edits
    )
    {
        foreach (AgentPromptSectionEdit edit in edits)
        {
            int index = sections.FindIndex(section =>
                string.Equals(section.Name, edit.Name, StringComparison.Ordinal)
            );
            if (edit.Text is null)
            {
                if (index >= 0)
                {
                    sections.RemoveAt(index);
                }

                continue;
            }

            if (index >= 0)
            {
                sections[index] = new AgentPromptSection(edit.Name, edit.Text);
            }
            else
            {
                sections.Add(new AgentPromptSection(edit.Name, edit.Text));
            }
        }

        return sections;
    }

    private static string MessageText(ChatMessage message) =>
        string.Concat(message.Contents.OfType<TextContent>().Select(content => content.Text));

    private static ChatMessage ReplaceMessageText(ChatMessage message, string text) =>
        new(
            message.Role,
            [
                new TextContent(text),
                .. message.Contents.Where(content => content is not TextContent),
            ]
        );

    private static AgentContextMessage ToContextMessage(ChatMessage message) =>
        new(message.Role.Value, MessageText(message));

    private static List<ChatMessage> InsertAddedContext(
        List<ChatMessage> request,
        IReadOnlyList<AgentContextMessage> added
    )
    {
        if (added.Count == 0)
        {
            return request;
        }

        int systemCount = 0;
        while (systemCount < request.Count && request[systemCount].Role == ChatRole.System)
        {
            systemCount++;
        }

        List<ChatMessage> merged = [.. request.Take(systemCount)];
        merged.AddRange(
            added.Select(message => new ChatMessage(new ChatRole(message.Role), message.Text))
        );
        merged.AddRange(request.Skip(systemCount));
        return merged;
    }

    private List<ChatMessage> BuildRequest(IReadOnlyList<AgentPromptSection> sections)
    {
        List<ChatMessage> request = [];
        if (sections.Count > 0)
        {
            request.Add(
                new ChatMessage(
                    ChatRole.System,
                    string.Join("\n\n", sections.Select(section => section.Text))
                )
            );
        }

        request.AddRange(_history);
        return request;
    }

    private ChatOptions BuildOptions(IReadOnlyList<string>? activeTools)
    {
        if (activeTools is null)
        {
            return new() { Tools = [.. _tools.Declarations] };
        }

        HashSet<string> selected = new(activeTools, StringComparer.Ordinal);
        return new()
        {
            Tools =
            [
                .. _tools.Declarations.Where(declaration => selected.Contains(declaration.Name)),
            ],
        };
    }
}
