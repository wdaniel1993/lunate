using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using Lunate.Ai;
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
    private const string CacheCreationInputTokensKey = "CacheCreationInputTokens";

    private readonly IChatClient _client;
    private readonly ToolRegistry _tools;
    private readonly AgentHarnessOptions _options;
    private readonly SessionImages _images;
    private readonly ReadLedger _reads = new();
    private readonly List<ChatMessage> _history = [];
    private readonly List<FunctionCallContent> _danglingCalls = [];
    private readonly ConcurrentDictionary<string, int> _spillSequenceByCallId = new(
        StringComparer.Ordinal
    );
    private int _runActive;
    private int _nestedCallCounter;
    private int _spillSequence;
    private SubagentTreeState? _treeState;
    private SubagentRunner? _subagentRunner;
    private readonly UsageModelClient? _compactionModel;
    private long _tokenBudgetUsed;
    private string? _lastModelId;

    public AgentHarness(IChatClient client, ToolRegistry tools, AgentHarnessOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(tools);
        _client = client;
        _tools = tools;
        _options = options ?? new AgentHarnessOptions();
        ValidateOptions(_options);
        _images = _options.Session?.Images ?? SessionImages.InMemory();
        _compactionModel = ResolveUsageModel("compaction");
        _compactor = new CompactionReducer(
            _compactionModel?.Client ?? _client,
            _options.CompactionKeepTurns
        );
        if (_options.Session is { } session)
        {
            foreach (SessionHistoryItem item in session.HistoryItems())
            {
                _history.Add(item.Message);
                _historyEntryIds.Add(item.EntryId);
                _historyModels.Add(item.Model);
            }

            SeedReadLedger(_history);
            _images.ValidateReferences(ImageReferences(_history));

            if (session.ActiveCompaction is { } compaction)
            {
                _summaryMessage = _history[0];
                _replacedEntryIds = [.. compaction.Replaces];
            }

            _historyUtf8 = CompactionReducer.Utf8Length(_history);
            _lastModelId = ResumeResolvedModelId(session, _options, _historyModels);
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

    internal UsageModelClient? CompactionModel => _compactionModel;

    /// <summary>
    /// The subagent accounting of the current or most recent top-level run tree: tokens consumed
    /// by its child runs and live children. Zero before any run.
    /// </summary>
    public SubagentUsage SubagentUsage => _treeState?.Snapshot ?? default;

    /// <summary>Runs the loop for one text turn and streams its events; the stream completes when the run ends.</summary>
    [SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple overloads with optional parameters",
        Justification = "The first parameter type distinguishes the overloads; the token keeps its default like the text overload."
    )]
    public IAsyncEnumerable<AgentEvent> RunAsync(
        string userInput,
        CancellationToken ct = default
    ) => RunAsync(new UserInput(userInput), skill: null, RunIds.Next(), ct);

    /// <summary>
    /// Runs the loop for one user turn, including its images, and streams its events; the stream
    /// completes when the run ends.
    /// </summary>
    [SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple overloads with optional parameters",
        Justification = "The first parameter type distinguishes the overloads; the token keeps its default like the text overload."
    )]
    public IAsyncEnumerable<AgentEvent> RunAsync(
        UserInput userInput,
        CancellationToken ct = default
    ) => RunAsync(userInput, skill: null, RunIds.Next(), ct);

    /// <summary>
    /// Runs the loop for one skill invocation: the user turn carries the skill metadata into the
    /// session entry (the <c>/skill:&lt;name&gt;</c> path). Skill turns stay text-only.
    /// </summary>
    public IAsyncEnumerable<AgentEvent> RunSkillAsync(
        string userInput,
        SessionSkillMetadata skill,
        CancellationToken ct
    ) => RunAsync(new UserInput(userInput), skill, RunIds.Next(), ct);

    /// <summary>Runs the loop under a caller-provided run id (the subagent runner links sessions by it).</summary>
    internal IAsyncEnumerable<AgentEvent> RunAsync(
        string userInput,
        string runId,
        CancellationToken ct = default
    ) => RunAsync(new UserInput(userInput), skill: null, runId, ct);

    internal async IAsyncEnumerable<AgentEvent> RunAsync(
        UserInput userInput,
        SessionSkillMetadata? skill,
        string runId,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(userInput);
        ArgumentNullException.ThrowIfNull(userInput.Text);
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
            _spillSequenceByCallId.Clear();
            Interlocked.Exchange(ref _spillSequence, 0);
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
        UserInput userInput,
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
        UserInput userInput,
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
            AppendHistoryMessage(BuildUserMessage(userInput), skill: skill);
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
                _lastModelId = stream.ModelId;
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
        UsageDetails? usageDetails = null;
        long requestUtf8 = 0;

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

        void EmitText(string text)
        {
            CloseReasoning();
            if (!textOpen)
            {
                textOpen = true;
                messageId = RunIds.NextMessage();
                channel.Emit(new TextMessageStart(runId, messageId));
            }

            channel.Emit(new TextMessageContent(runId, messageId!, text));
        }

        void EmitReasoning(string text)
        {
            CloseText();
            if (!reasoningOpen)
            {
                reasoningOpen = true;
                reasoningMessageId = RunIds.NextMessage();
                channel.Emit(new ReasoningStart(runId, reasoningMessageId));
            }

            channel.Emit(new ReasoningMessageContent(runId, reasoningMessageId!, text));
        }

        void EmitSegment(InlineThinkingSplitter.Segment segment)
        {
            if (segment.IsReasoning)
            {
                EmitReasoning(segment.Text);
            }
            else
            {
                EmitText(segment.Text);
            }
        }

        // Style `inline` models embed their reasoning in the text stream (design D4); every other
        // model's text flows straight through.
        InlineThinkingSplitter? inlineSplitter = DeclaredModel() is { Reasoning: "inline" }
            ? new InlineThinkingSplitter()
            : null;
        UsageModelClient? usageModel = ResolveUsageModel(_options.Usage);

        try
        {
            List<ChatMessage> request = BuildRequest(sections);
            requestUtf8 = _historyUtf8 + SystemPromptUtf8(sections);
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
                    ChatResponseUpdate update in (
                        usageModel?.Client ?? _client
                    ).GetStreamingResponseAsync(request, BuildOptions(activeTools), ct)
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
                            attempt.Emitted = true;
                            EmitReasoning(reasoning);
                        }
                        else if (content is TextContent { Text: { Length: > 0 } text })
                        {
                            attempt.Emitted = true;
                            if (inlineSplitter is null)
                            {
                                EmitText(text);
                            }
                            else
                            {
                                foreach (
                                    InlineThinkingSplitter.Segment segment in inlineSplitter.Feed(
                                        text
                                    )
                                )
                                {
                                    EmitSegment(segment);
                                }
                            }
                        }
                        else if (content is UsageContent usageContent)
                        {
                            attempt.Emitted = true;
                            usageDetails = usageContent.Details;
                        }
                    }
                }
            }
        }
        finally
        {
            if (inlineSplitter is { } splitter)
            {
                foreach (InlineThinkingSplitter.Segment segment in splitter.Flush())
                {
                    EmitSegment(segment);
                }
            }

            CloseReasoning();
            CloseText();
        }

        if (usageDetails is { } reportedUsage)
        {
            ModelUsageAttribution? attribution = UsageAttribution(_options.Usage, usageModel);
            AccumulateUsage(
                runId,
                channel,
                reportedUsage,
                routedModelId ?? modelId ?? usageModel?.Model.Id ?? _options.ModelId,
                attribution
            );
            usage = RecordedUsage(reportedUsage, attribution);
            _lastInputTokens = usage.Input;
            _lastRequestUtf8 = requestUtf8;
        }

        return new ModelStreamResult(
            updates,
            finishReason,
            attempt.Emitted,
            routedModelId ?? modelId ?? usageModel?.Model.Id,
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
        string? modelId,
        ModelUsageAttribution? attribution = null
    )
    {
        channel.Emit(
            new UsageUpdated(runId, details) { ModelId = modelId, Attribution = attribution }
        );
        long used = (details.InputTokenCount ?? 0) + (details.OutputTokenCount ?? 0);
        _tokenBudgetUsed += used;
        InheritedTreeState?.AddTokens(used);
    }

    private UsageModelClient? ResolveUsageModel(string usage)
    {
        IReadOnlyDictionary<string, UsageModelClient>? models = _options.UsageModels;
        if (models is null)
        {
            return null;
        }

        if (models.TryGetValue(usage, out UsageModelClient? resolved))
        {
            return resolved;
        }

        return usage is "subagents" or "compaction" && models.TryGetValue("chat", out resolved)
            ? resolved
            : null;
    }

    private static ModelUsageAttribution? UsageAttribution(
        string usage,
        UsageModelClient? model,
        string? decisionMethod = null,
        double? probability = null
    ) =>
        model is null
            ? null
            : new ModelUsageAttribution(
                usage,
                model.ProfileName,
                model.ProviderName,
                model.ProviderKind,
                decisionMethod,
                probability
            );

    /// <summary>
    /// Maps one provider usage record onto the session's token lanes. The input/output totals stay
    /// the provider's counts; a cache lane is recorded only when the provider reports a positive
    /// count, so an unreported lane stays unknown rather than zero.
    /// </summary>
    private static SessionUsage RecordedUsage(
        UsageDetails details,
        ModelUsageAttribution? attribution = null
    )
    {
        int? cacheRead = details.CachedInputTokenCount is > 0 and long reportedRead
            ? (int)reportedRead
            : null;
        int? cacheWrite = null;
        if (
            details.AdditionalCounts is { } counts
            && counts.TryGetValue(CacheCreationInputTokensKey, out long reportedWrite)
            && reportedWrite > 0
        )
        {
            cacheWrite = (int)reportedWrite;
        }

        return new SessionUsage(
            (int)(details.InputTokenCount ?? 0),
            (int)(details.OutputTokenCount ?? 0),
            cacheRead,
            cacheWrite
        )
        {
            Attribution = attribution,
        };
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
            channel.Emit(new SteeringInjected(runId, entryId, message.Text));
        }
    }

    /// <summary>
    /// The user turn as the history records it: text only without images, else the text plus one
    /// image reference per attachment, stored before the message is appended.
    /// </summary>
    private ChatMessage BuildUserMessage(UserInput userInput)
    {
        if (userInput.Images is not { Count: > 0 } images)
        {
            return new ChatMessage(ChatRole.User, userInput.Text);
        }

        List<AIContent> contents = [new TextContent(userInput.Text)];
        foreach (ImageAttachment image in images)
        {
            contents.Add(_images.Store(image.Data, image.MimeType, image.Width, image.Height));
        }

        return new ChatMessage(ChatRole.User, contents);
    }

    /// <summary>
    /// The admission mask, the first step of a tool result's joining the conversation: the
    /// host-supplied <see cref="AgentHarnessOptions.ToolOutputMask"/> replaces the values it
    /// holds before the result is truncated, emitted, appended or spilled. The host owns the
    /// wiring — it stands in for the same redactor for session and no-session runs; the
    /// session's redactor keeps its persistence role at rest, and a null mask is the identity.
    /// </summary>
    private string RedactForContext(string output) =>
        _options.ToolOutputMask?.Invoke(output) ?? output;

    /// <summary>Every image reference a message carries.</summary>
    private static IEnumerable<ImageContentReference> ImageReferences(
        IEnumerable<ChatMessage> messages
    ) => messages.SelectMany(message => message.Contents).OfType<ImageContentReference>();

    /// <summary>
    /// Appends a conversation message outside a run: a typed bash-mode exchange becomes context
    /// for the next request exactly like a message the loop appended.
    /// </summary>
    /// <param name="message">The message to append.</param>
    /// <param name="model">The model that produced it, when one did.</param>
    public void AppendConversationMessage(ChatMessage message, string? model = null) =>
        AppendHistoryMessage(message, model);

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
        _historyModels.Add(model);
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

    /// <summary>
    /// Refuses a request whose history carries image references when the active model cannot take
    /// images. The gate runs before hydration, so a text-only model never reads image bytes (and a
    /// resumed session fails the same way a live turn does, instead of sending references it cannot
    /// encode). A resumed image also fails before the provider is called at all.
    /// </summary>
    private void EnsureImagesSupported()
    {
        int count = ImageReferences(_history).Count();
        if (count == 0)
        {
            return;
        }

        ModelInfo? model = _options.Model ?? DeclaredModel();
        if (model is null)
        {
            return;
        }

        if (model.Capabilities?.InputModalities is { } modalities)
        {
            if (modalities.Contains("image"))
            {
                return;
            }
        }
        else if (model.SupportsVision)
        {
            return;
        }
        else
        {
            return;
        }

        string id = KnownModelId(model?.Id) ?? KnownModelId(_options.ModelId) ?? "unknown";
        throw new InvalidDataException(
            $"model {id} doesn't accept images; switch profile or remove the image."
        );
    }

    private List<ChatMessage> BuildRequest(IReadOnlyList<AgentPromptSection> sections)
    {
        StubStaleReadResults();
        EnsureUsageCapabilities();
        EnsureImagesSupported();

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

        string? currentModelId = CurrentModelId();
        for (int index = 0; index < _history.Count; index++)
        {
            ChatMessage message = _history[index];
            if (ReasoningCrossesModels(_historyModels[index], currentModelId))
            {
                message = WithoutReasoning(message);
            }

            request.Add(HydrateImages(message));
        }

        return request;
    }

    /// <summary>
    /// The request copy of a message with every image reference replaced by its bytes; the stored
    /// message (and the session entry) keeps the reference. Messages without images are returned
    /// as they are.
    /// </summary>
    private ChatMessage HydrateImages(ChatMessage message)
    {
        if (!message.Contents.OfType<ImageContentReference>().Any())
        {
            return message;
        }

        ChatMessage copy = message.Clone();
        copy.Contents =
        [
            .. copy.Contents.Select(content =>
                content is ImageContentReference reference ? ReadImage(reference) : content
            ),
        ];
        return copy;
    }

    /// <summary>
    /// One reference's bytes as provider content. A reference without bytes fails the request
    /// rather than silently dropping the image.
    /// </summary>
    private DataContent ReadImage(ImageContentReference reference)
    {
        if (_images.TryRead(reference, out ReadOnlyMemory<byte> data))
        {
            return new DataContent(data, reference.MimeType);
        }

        string location = _images.IsInMemory
            ? "the run's in-memory image store"
            : $"the session blob at '{_images.BlobPath(reference)}'";
        throw new InvalidDataException(
            $"Session image '{reference.Digest}' is missing from {location}. Re-attach the image or start a new session."
        );
    }

    /// <summary>
    /// The model id of the next request: the id the last model call reported (the resolved model
    /// after routing) — seeded from the session at resume — else the configured model. A blank id
    /// counts as unknown.
    /// </summary>
    private string? CurrentModelId() =>
        KnownModelId(_lastModelId) ?? KnownModelId(_options.ModelId);

    /// <summary>
    /// The resolved model id a resumed session's history is compared against (design D6): the
    /// most recent recorded id, or null when the configured id is the resolved id itself. A
    /// configured id the catalog declares is what every call records, so nothing needs seeding;
    /// any other configured id is a request id (a virtual or extension model) whose resolution
    /// only the session records, so the last recorded id seeds the filter until the first call
    /// reports the real resolution. A session with model changes is mixed — the recorded ids'
    /// provenance is ambiguous — and contributes no seed.
    /// </summary>
    private static string? ResumeResolvedModelId(
        Session session,
        AgentHarnessOptions options,
        IReadOnlyList<string?> recordedModelIds
    )
    {
        if (
            KnownModelId(options.ModelId) is not { } configured
            || options.ModelCatalog is not { } catalog
            || catalog.Find(configured) is not null
            || session.Entries.OfType<SessionModelChangeEntry>().Any()
        )
        {
            return null;
        }

        for (int index = recordedModelIds.Count - 1; index >= 0; index--)
        {
            if (KnownModelId(recordedModelIds[index]) is { } resolved)
            {
                return resolved;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the recorded model id makes reasoning content foreign to the current request: the
    /// safety rule of design D6. Unknown ids on either side keep the content.
    /// </summary>
    private static bool ReasoningCrossesModels(string? recordedModelId, string? currentModelId) =>
        KnownModelId(recordedModelId) is { } recorded
        && KnownModelId(currentModelId) is { } current
        && !string.Equals(recorded, current, StringComparison.Ordinal);

    private static string? KnownModelId(string? modelId) =>
        string.IsNullOrWhiteSpace(modelId) ? null : modelId;

    /// <summary>
    /// The request copy of a history message with its reasoning content removed; the stored message
    /// is left untouched. Messages without reasoning are returned as they are.
    /// </summary>
    private static ChatMessage WithoutReasoning(ChatMessage message)
    {
        if (!message.Contents.OfType<TextReasoningContent>().Any())
        {
            return message;
        }

        ChatMessage copy = message.Clone();
        copy.Contents = [.. copy.Contents.Where(content => content is not TextReasoningContent)];
        return copy;
    }

    private ChatOptions BuildOptions(IReadOnlyList<string>? activeTools)
    {
        ChatOptions options;
        if (activeTools is null)
        {
            options = new() { Tools = [.. _tools.Declarations] };
        }
        else
        {
            HashSet<string> selected = new(activeTools, StringComparer.Ordinal);
            options = new()
            {
                Tools =
                [
                    .. _tools.Declarations.Where(declaration =>
                        selected.Contains(declaration.Name)
                    ),
                ],
            };
        }

        if (DeclaredModel() is { } model)
        {
            ReasoningParameters.Apply(options, model, _options.ThinkingLevel);
        }

        return options;
    }

    /// <summary>The catalog entry of the harness's model; null when no catalog declares it.</summary>
    private ModelInfo? DeclaredModel() =>
        ResolveUsageModel(_options.Usage)?.Model
        ?? _options.Model
        ?? (_options.ModelId is { } id ? _options.ModelCatalog?.Find(id) : null);
}
