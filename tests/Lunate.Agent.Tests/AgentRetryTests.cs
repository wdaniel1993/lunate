namespace Lunate.Agent.Tests;

public sealed class AgentRetryTests
{
    [Fact]
    public async Task A_transient_failure_retries_and_the_run_succeeds()
    {
        var client = new ScriptedChatClient()
            .EnqueueFailure(new HttpRequestException("connection reset"))
            .Enqueue(LoopScripts.Text("Recovered"), LoopScripts.Stop());
        var harness = new AgentHarness(client, new ToolRegistry(), FastRetry());

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        Retrying retry = Assert.Single(events.OfType<Retrying>());
        Assert.Equal(1, retry.Attempt);
        Assert.Contains("connection reset", retry.Reason);
        Assert.Equal(StopReasons.Stop, Assert.IsType<RunFinished>(events[^1]).StopReason);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task Exhausted_retries_end_with_a_run_error()
    {
        var client = new ScriptedChatClient();
        for (int attempt = 0; attempt < 4; attempt++)
        {
            client.EnqueueFailure(new TaskCanceledException("provider timed out"));
        }

        var harness = new AgentHarness(client, new ToolRegistry(), FastRetry());

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        Assert.Equal([1, 2, 3], events.OfType<Retrying>().Select(retrying => retrying.Attempt));
        Assert.Contains("provider timed out", Assert.IsType<RunError>(events[^1]).Message);
        Assert.Equal(4, client.Requests.Count);
    }

    [Fact]
    public async Task A_non_retryable_failure_ends_the_run_immediately()
    {
        var client = new ScriptedChatClient().EnqueueFailure(
            new InvalidOperationException("bad request")
        );
        var harness = new AgentHarness(client, new ToolRegistry(), FastRetry());

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Empty(events.OfType<Retrying>());
        Assert.Equal("Run failed: bad request", Assert.IsType<RunError>(events[^1]).Message);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task A_failure_after_emitting_text_is_not_retried()
    {
        var client = new ScriptedChatClient().EnqueueFailure(
            new HttpRequestException("dropped mid-stream"),
            LoopScripts.Text("partial")
        );
        var harness = new AgentHarness(client, new ToolRegistry(), FastRetry());

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Empty(events.OfType<Retrying>());
        Assert.Single(events.OfType<TextMessageStart>());
        Assert.Single(events.OfType<TextMessageEnd>());
        Assert.IsType<RunError>(events[^1]);
        Assert.Single(client.Requests);
    }

    private static AgentHarnessOptions FastRetry() => new() { RetryBaseDelay = TimeSpan.Zero };
}
