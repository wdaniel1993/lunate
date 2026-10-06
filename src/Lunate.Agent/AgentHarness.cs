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

    public AgentHarness(IChatClient client, ToolRegistry tools, AgentHarnessOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(tools);
        _client = client;
        _tools = tools;
        _options = options ?? new AgentHarnessOptions();
        ValidateOptions(_options);
        if (_options.Session is { } session)
        {
            _history.AddRange(session.ToHistory());
        }
    }

    /// <summary>Runs the loop for one user input and streams its events; the stream completes when the run ends.</summary>
    public async IAsyncEnumerable<AgentEvent> RunAsync(
        string userInput,
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
            string runId = RunIds.Next();
            _danglingCalls.Clear();
            var channel = new AgentEventChannel();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task loop = RunLoopAsync(runId, userInput, channel, linked.Token);
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
    }

    private async Task RunLoopAsync(
        string runId,
        string userInput,
        AgentEventChannel channel,
        CancellationToken ct
    )
    {
        try
        {
            await ExecuteRunAsync(runId, userInput, channel, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // ExecuteRunAsync turns user cancellation into RunFinished(cancelled); this guard
            // keeps the loop task from faulting should an OCE still escape.
        }
        finally
        {
            channel.Complete();
        }
    }

    private async Task ExecuteRunAsync(
        string runId,
        string userInput,
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
            AppendHistoryMessage(new ChatMessage(ChatRole.User, userInput));
            channel.Emit(new RunStarted(runId));
            runStarted = true;

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

                modelCalls++;
                ModelStreamResult stream = await StreamModelWithRetriesAsync(runId, channel, ct);
                foreach (ChatMessage message in stream.Updates.ToChatResponse().Messages)
                {
                    AppendHistoryMessage(message, stream.ModelId, stream.Usage);
                }

                List<FunctionCallContent> calls = DistinctCalls(stream.Updates);
                if (calls.Count == 0)
                {
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
        CancellationToken ct,
        ModelStreamAttempt attempt
    )
    {
        List<ChatResponseUpdate> updates = [];
        string? messageId = null;
        bool textOpen = false;
        ChatFinishReason? finishReason = null;
        string? modelId = null;
        SessionUsage? usage = null;

        try
        {
            await foreach (
                ChatResponseUpdate update in _client.GetStreamingResponseAsync(
                    BuildRequest(),
                    BuildOptions(),
                    ct
                )
            )
            {
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
                    if (content is TextContent { Text: { Length: > 0 } text })
                    {
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
                        channel.Emit(new UsageUpdated(runId, usageContent.Details));
                        usage = new SessionUsage(
                            (int)(usageContent.Details.InputTokenCount ?? 0),
                            (int)(usageContent.Details.OutputTokenCount ?? 0)
                        );
                    }
                }
            }
        }
        finally
        {
            if (textOpen)
            {
                channel.Emit(new TextMessageEnd(runId, messageId!));
            }
        }

        return new ModelStreamResult(updates, finishReason, attempt.Emitted, modelId, usage);
    }

    /// <summary>Appends a history message and mirrors it into the session when one is attached.</summary>
    private void AppendHistoryMessage(
        ChatMessage message,
        string? model = null,
        SessionUsage? usage = null
    )
    {
        _history.Add(message);
        _options.Session?.AppendMessage(message, model, usage);
    }

    private static string MappedStopReason(ChatFinishReason? finishReason) =>
        finishReason == ChatFinishReason.Length ? StopReasons.Length : StopReasons.Stop;

    private List<ChatMessage> BuildRequest()
    {
        List<ChatMessage> request = [];
        if (_options.SystemPrompt is not null)
        {
            request.Add(new ChatMessage(ChatRole.System, _options.SystemPrompt));
        }

        request.AddRange(_history);
        return request;
    }

    private ChatOptions BuildOptions() => new() { Tools = [.. _tools.Declarations] };
}
