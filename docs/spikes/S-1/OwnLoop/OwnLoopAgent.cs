using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Spike.Shared;

namespace Spike.OwnLoop;

public sealed record ApprovalContext(string CallId, SpikeTool Tool, string ArgsJson);

public delegate ValueTask<ApprovalDecision> ApprovalCallback(ApprovalContext context, CancellationToken cancellationToken);

public sealed class OwnLoopAgent(
    IChatClient client,
    IReadOnlyList<SpikeTool> tools,
    string systemPrompt,
    ApprovalCallback approval,
    int maxSteps = 20)
{
    private readonly List<ChatMessage> _history = [];
    private readonly HashSet<string> _sessionApproved = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _steering = new();
    private int _runCounter;

    public IReadOnlyList<ChatMessage> History => this._history;

    public void Steer(string message) => this._steering.Enqueue(message);

    public async IAsyncEnumerable<OwnLoopEvent> RunAsync(
        string userInput,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string runId = $"run_{++this._runCounter}";
        this._history.Add(new ChatMessage(ChatRole.User, userInput));
        yield return new RunStarted(runId);

        RunStopReason stopReason = RunStopReason.Completed;

        for (int step = 0; step < maxSteps; step++)
        {
            while (this._steering.TryDequeue(out string? steering))
            {
                this._history.Add(new ChatMessage(ChatRole.User, steering));
                yield return new SteeringInjected(steering);
            }

            StreamAccumulator accumulator = new(step);
            List<OwnLoopEvent> buffered = [];
            bool cancelled = false;

            try
            {
                List<ChatMessage> messages = [new ChatMessage(ChatRole.System, systemPrompt), .. this._history];
                ChatOptions options = new() { Tools = tools.Select(t => (AITool)t.Declare()).ToList() };
                await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(messages, options, cancellationToken))
                {
                    buffered.AddRange(accumulator.Process(update));
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            buffered.AddRange(accumulator.CompleteTurn());
            foreach (OwnLoopEvent item in buffered)
            {
                yield return item;
            }

            List<AccumulatedCall> calls = accumulator.CompleteCalls();
            this._history.Add(accumulator.ToAssistantMessage());

            if (cancelled)
            {
                foreach (AccumulatedCall call in calls)
                {
                    const string message = "Cancelled by the user before execution.";
                    yield return new ToolCallStart(call.CallId, call.Name);
                    yield return new ToolCallArgs(call.CallId, call.ArgumentsJson);
                    yield return new ToolCallEnd(call.CallId);
                    yield return new ToolCallResult(call.CallId, message, IsError: true);
                    this._history.Add(ToolMessage(call.CallId, message));
                }

                stopReason = RunStopReason.Cancelled;
                break;
            }

            if (calls.Count == 0)
            {
                break;
            }

            bool cancelledDuringTools = false;
            foreach (AccumulatedCall call in calls)
            {
                List<OwnLoopEvent> toolEvents = [];
                (bool toolCancelled, string output) = await this.ExecuteCallAsync(call, toolEvents, cancellationToken);
                foreach (OwnLoopEvent item in toolEvents)
                {
                    yield return item;
                }

                this._history.Add(ToolMessage(call.CallId, output));

                if (toolCancelled)
                {
                    cancelledDuringTools = true;
                    break;
                }
            }

            if (cancelledDuringTools)
            {
                stopReason = RunStopReason.Cancelled;
                break;
            }

            if (step == maxSteps - 1)
            {
                stopReason = RunStopReason.StepLimit;
            }
        }

        yield return new RunFinished(stopReason);
    }

    private static ChatMessage ToolMessage(string callId, string output) =>
        new(ChatRole.Tool, [new FunctionResultContent(callId, output)]);

    private async Task<(bool Cancelled, string Output)> ExecuteCallAsync(
        AccumulatedCall call,
        List<OwnLoopEvent> events,
        CancellationToken cancellationToken)
    {
        events.Add(new ToolCallStart(call.CallId, call.Name));
        events.Add(new ToolCallArgs(call.CallId, call.ArgumentsJson));
        events.Add(new ToolCallEnd(call.CallId));

        SpikeTool? tool = tools.FirstOrDefault(t => t.Name == call.Name);
        if (tool is null)
        {
            string message = $"Unknown tool '{call.Name}'. Available tools: {string.Join(", ", tools.Select(t => t.Name))}.";
            events.Add(new ToolCallResult(call.CallId, message, IsError: true));
            return (false, message);
        }

        if (!call.TryGetArguments(out var arguments, out string? parseError))
        {
            string message = $"Invalid JSON arguments for '{call.Name}': {parseError}. Fix the arguments and retry.";
            events.Add(new ToolCallResult(call.CallId, message, IsError: true));
            return (false, message);
        }

        if (tool.Risk != SpikeToolRisk.ReadOnly && !this._sessionApproved.Contains(tool.Name))
        {
            events.Add(new ApprovalRequested(call.CallId, tool.Name, call.ArgumentsJson));

            ApprovalDecision decision;
            try
            {
                decision = await approval(new ApprovalContext(call.CallId, tool, call.ArgumentsJson), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                events.Add(new ApprovalDecided(call.CallId, ApprovalDecision.Deny));
                const string cancelledMessage = "Cancelled by the user while waiting for approval.";
                events.Add(new ToolCallResult(call.CallId, cancelledMessage, IsError: true));
                return (true, cancelledMessage);
            }

            events.Add(new ApprovalDecided(call.CallId, decision));

            if (decision == ApprovalDecision.Deny)
            {
                string denied = $"Denied by the user: '{tool.Name}' was not run. Explain why the call is needed and ask before retrying.";
                events.Add(new ToolCallResult(call.CallId, denied, IsError: true));
                return (false, denied);
            }

            if (decision == ApprovalDecision.AllowForSession)
            {
                this._sessionApproved.Add(tool.Name);
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string output = tool.Stub(arguments);
            events.Add(new ToolCallResult(call.CallId, output, IsError: false));
            return (false, output);
        }
        catch (OperationCanceledException)
        {
            const string cancelledMessage = "Cancelled by the user during execution.";
            events.Add(new ToolCallResult(call.CallId, cancelledMessage, IsError: true));
            return (true, cancelledMessage);
        }
        catch (Exception ex)
        {
            string message = $"Tool '{tool.Name}' failed: {ex.Message}";
            events.Add(new ToolCallResult(call.CallId, message, IsError: true));
            return (false, message);
        }
    }
}
