using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace SubagentsExtension.Tests;

/// <summary>
/// A local scripted model client that records every request, with optional gates so a test can hold
/// one model call (parent or child) while it inspects or cancels the run.
/// </summary>
internal sealed class ScriptedModelClient : IChatClient
{
    private readonly Queue<ChatResponseUpdate[]> _scripts = new();
    private readonly Dictionary<int, TaskCompletionSource> _started = new();
    private readonly Dictionary<int, TaskCompletionSource> _release = new();

    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    /// <summary>Queues one script; several groups concatenate into one model response.</summary>
    public ScriptedModelClient Enqueue(params ChatResponseUpdate[][] groups)
    {
        _scripts.Enqueue([.. groups.SelectMany(group => group)]);
        return this;
    }

    /// <summary>The start of one call as an awaitable; 1-based over the call order.</summary>
    public Task WaitForCallAsync(int call)
    {
        lock (_started)
        {
            return Started(call).Task;
        }
    }

    /// <summary>Holds the given call until <see cref="Release"/> is called.</summary>
    public void Gate(int call)
    {
        lock (_release)
        {
            _release[call] = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
        }
    }

    /// <summary>Releases a gated call.</summary>
    public void Release(int call)
    {
        lock (_release)
        {
            if (_release.TryGetValue(call, out TaskCompletionSource? gate))
            {
                gate.TrySetResult();
            }
        }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new ChatResponse());

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        int call;
        Task? gate;
        ChatResponseUpdate[] script;
        lock (_started)
        {
            Requests.Add([.. messages]);
            call = Requests.Count;
            Started(call).TrySetResult();
        }

        lock (_release)
        {
            gate = _release.TryGetValue(call, out TaskCompletionSource? pending)
                ? pending.Task
                : null;
        }

        if (gate is not null)
        {
            await gate.WaitAsync(cancellationToken);
        }

        script =
            _scripts.Count > 0
                ? _scripts.Dequeue()
                : throw new InvalidOperationException(
                    "ScriptedModelClient has no scripted response left."
                );
        foreach (ChatResponseUpdate update in script)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private TaskCompletionSource Started(int call)
    {
        if (!_started.TryGetValue(call, out TaskCompletionSource? started))
        {
            _started[call] = started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
        }

        return started;
    }
}

/// <summary>The scripted model turns the tests enqueue.</summary>
internal static class ModelScripts
{
    public static ChatResponseUpdate[] Call(
        string callId,
        string toolName,
        Dictionary<string, object?> arguments
    ) =>
        [
            new ChatResponseUpdate(
                ChatRole.Assistant,
                [new FunctionCallContent(callId, toolName, arguments)]
            ),
            new ChatResponseUpdate(ChatRole.Assistant, [])
            {
                FinishReason = ChatFinishReason.ToolCalls,
            },
        ];

    public static ChatResponseUpdate[] Text(string text) =>
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(text)])
            {
                FinishReason = ChatFinishReason.Stop,
            },
        ];

    public static ChatResponseUpdate[] Usage(long input, long output) =>
        [
            new ChatResponseUpdate(
                ChatRole.Assistant,
                [
                    new UsageContent(
                        new UsageDetails { InputTokenCount = input, OutputTokenCount = output }
                    ),
                ]
            ),
        ];

    public static Dictionary<string, object?> Args(params (string Key, object? Value)[] entries)
    {
        var arguments = new Dictionary<string, object?>();
        foreach ((string key, object? value) in entries)
        {
            arguments[key] = value;
        }

        return arguments;
    }
}
