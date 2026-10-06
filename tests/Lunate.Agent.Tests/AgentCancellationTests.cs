using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class AgentCancellationTests
{
    [Fact]
    public async Task Cancelling_mid_tool_repairs_the_history_and_finishes_as_cancelled()
    {
        var tool = new BlockingTool("wait");
        var client = new ScriptedChatClient().Enqueue(
            LoopScripts.Call("call_1", "wait", LoopScripts.Args(("path", "a.txt"))),
            LoopScripts.ToolCalls()
        );
        var harness = new AgentHarness(client, Registry(tool));
        using var cancellation = new CancellationTokenSource();

        Task<List<AgentEvent>> run = harness
            .RunAsync("go", cancellation.Token)
            .ToListAsync(TestContext.Current.CancellationToken);
        await tool.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        cancellation.Cancel();
        List<AgentEvent> events = await run;

        Assert.Empty(EventSequenceValidator.Validate(events));
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.True(result.IsError);
        Assert.Contains("cancelled by the user", result.Output);
        Assert.Equal(StopReasons.Cancelled, Assert.IsType<RunFinished>(events[^1]).StopReason);

        client.Enqueue(LoopScripts.Text("Recovered"), LoopScripts.Stop());
        await harness
            .RunAsync("again", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        ChatMessage toolMessage = client
            .Requests[1]
            .Messages.Single(message => message.Role == ChatRole.Tool);
        string repaired = Assert
            .IsType<FunctionResultContent>(Assert.Single(toolMessage.Contents))
            .Result!.ToString()!;
        Assert.Contains("cancelled by the user", repaired);
    }

    [Fact]
    public async Task Cancelling_mid_stream_closes_the_text_message_and_finishes_as_cancelled()
    {
        var client = new BlockingStreamClient();
        var harness = new AgentHarness(client, new ToolRegistry());
        using var cancellation = new CancellationTokenSource();

        Task<List<AgentEvent>> run = harness
            .RunAsync("go", cancellation.Token)
            .ToListAsync(TestContext.Current.CancellationToken);
        await client.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        cancellation.Cancel();
        List<AgentEvent> events = await run;

        Assert.Empty(EventSequenceValidator.Validate(events));
        Assert.Single(events.OfType<TextMessageStart>());
        Assert.Single(events.OfType<TextMessageEnd>());
        Assert.Equal(StopReasons.Cancelled, Assert.IsType<RunFinished>(events[^1]).StopReason);
    }

    [Fact]
    public async Task Abandoning_the_stream_stops_the_run_and_the_harness_accepts_a_new_run()
    {
        var client = new AbandonableChatClient();
        var harness = new AgentHarness(client, new ToolRegistry());

        await using (
            IAsyncEnumerator<AgentEvent> enumerator = harness
                .RunAsync("go", TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken)
        )
        {
            Assert.True(await enumerator.MoveNextAsync());
            Assert.IsType<RunStarted>(enumerator.Current);
        }

        await client.FirstCallStopped.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(1, client.CallCount);

        List<AgentEvent> events = await harness
            .RunAsync("again", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(StopReasons.Stop, Assert.IsType<RunFinished>(events[^1]).StopReason);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task A_second_concurrent_run_is_rejected()
    {
        var client = new AbandonableChatClient();
        var harness = new AgentHarness(client, new ToolRegistry());

        await using IAsyncEnumerator<AgentEvent> first = harness
            .RunAsync("one", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await first.MoveNextAsync());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
            {
                await using IAsyncEnumerator<AgentEvent> second = harness
                    .RunAsync("two", TestContext.Current.CancellationToken)
                    .GetAsyncEnumerator(TestContext.Current.CancellationToken);
                await second.MoveNextAsync();
            }
        );

        Assert.Contains("sequential", exception.Message);
    }

    private sealed class BlockingTool(string name) : ITool
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name { get; } = name;

        public string Description => "Blocks until the run is cancelled.";

        public string SchemaJson => """{"type":"object"}""";

        public JsonElement ParametersSchema { get; } = ParseSchema();

        public ToolRisk Risk => ToolRisk.ReadOnly;

        public async Task<ToolResult> ExecuteAsync(
            JsonElement args,
            ToolContext ctx,
            CancellationToken ct
        )
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new ToolResult("done", IsError: false);
        }

        private static JsonElement ParseSchema()
        {
            using JsonDocument document = JsonDocument.Parse("""{"type":"object"}""");
            return document.RootElement.Clone();
        }
    }

    private sealed class BlockingStreamClient : IChatClient
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            yield return LoopScripts.Text("partial");
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield return LoopScripts.Stop();
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class AbandonableChatClient : IChatClient
    {
        private int _calls;

        public TaskCompletionSource FirstCallStopped { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => _calls;

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            int call = Interlocked.Increment(ref _calls);
            if (call == 1)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    FirstCallStopped.TrySetResult();
                    throw;
                }

                yield break;
            }

            yield return LoopScripts.Text("Done");
            yield return LoopScripts.Stop();
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
