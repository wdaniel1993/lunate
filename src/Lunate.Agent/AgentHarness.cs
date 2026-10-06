using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// The agent loop: a turn on an <see cref="IChatClient"/> that streams model output as events,
/// runs tools itself (ADR-0003) and keeps the history valid. Runs on one harness are sequential;
/// concurrent runs are a later, explicit decision.
/// </summary>
public sealed partial class AgentHarness
{
    private readonly IChatClient _client;
    private readonly ToolRegistry _tools;
    private readonly AgentHarnessOptions _options;
    private readonly List<ChatMessage> _history = [];

    public AgentHarness(IChatClient client, ToolRegistry tools, AgentHarnessOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(tools);
        _client = client;
        _tools = tools;
        _options = options ?? new AgentHarnessOptions();
    }

    /// <summary>Runs one turn and streams its events; the stream completes when the run ends.</summary>
    public async IAsyncEnumerable<AgentEvent> RunAsync(
        string userInput,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(userInput);
        string runId = RunIds.Next();
        var channel = new AgentEventChannel();
        Task loop = RunLoopAsync(runId, userInput, channel, ct);
        await foreach (AgentEvent agentEvent in channel.ReadAllAsync(ct))
        {
            yield return agentEvent;
        }

        await loop;
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
        catch (OperationCanceledException)
        {
            // Cancellation is not a run error (T-10); the channel still completes.
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
        try
        {
            _history.Add(new ChatMessage(ChatRole.User, userInput));
            channel.Emit(new RunStarted(runId));

            while (true)
            {
                List<ChatResponseUpdate> updates = await StreamModelAsync(runId, channel, ct);
                _history.AddRange(updates.ToChatResponse().Messages);

                List<FunctionCallContent> calls = CompleteCalls(updates);
                if (calls.Count == 0)
                {
                    channel.Emit(new RunFinished(runId, StopReasons.Stop));
                    return;
                }

                foreach (FunctionCallContent call in calls)
                {
                    await ExecuteCallAsync(runId, call, channel, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            channel.Emit(new RunError(runId, $"Run failed: {exception.Message}"));
        }
    }

    private async Task<List<ChatResponseUpdate>> StreamModelAsync(
        string runId,
        AgentEventChannel channel,
        CancellationToken ct
    )
    {
        List<ChatResponseUpdate> updates = [];
        string? messageId = null;
        bool textOpen = false;

        await foreach (
            ChatResponseUpdate update in _client.GetStreamingResponseAsync(
                BuildRequest(),
                BuildOptions(),
                ct
            )
        )
        {
            updates.Add(update);
            foreach (AIContent content in update.Contents)
            {
                if (content is not TextContent { Text: { Length: > 0 } text })
                {
                    continue;
                }

                if (!textOpen)
                {
                    textOpen = true;
                    messageId = RunIds.NextMessage();
                    channel.Emit(new TextMessageStart(runId, messageId));
                }

                channel.Emit(new TextMessageContent(runId, messageId!, text));
            }
        }

        if (textOpen)
        {
            channel.Emit(new TextMessageEnd(runId, messageId!));
        }

        return updates;
    }

    private static List<FunctionCallContent> CompleteCalls(List<ChatResponseUpdate> updates)
    {
        Dictionary<string, FunctionCallContent> byCallId = new(StringComparer.Ordinal);
        List<string> order = [];
        foreach (ChatResponseUpdate update in updates)
        {
            foreach (AIContent content in update.Contents)
            {
                if (content is not FunctionCallContent call)
                {
                    continue;
                }

                string callId = call.CallId ?? string.Empty;
                if (byCallId.TryAdd(callId, call))
                {
                    order.Add(callId);
                }
            }
        }

        return [.. order.Select(callId => byCallId[callId])];
    }

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
