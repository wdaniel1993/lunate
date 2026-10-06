namespace Lunate.Agent.Tests;

public sealed class AgentEventChannelTests
{
    private const string RunId = "run_1";

    [Fact]
    public async Task ReadAllAsync_yields_events_in_emission_order()
    {
        var channel = new AgentEventChannel();
        AgentEvent started = new RunStarted(RunId);
        AgentEvent text = new TextMessageContent(RunId, "msg_1", "hello");
        AgentEvent finished = new RunFinished(RunId, StopReasons.Stop);
        channel.Emit(started);
        channel.Emit(text);
        channel.Emit(finished);
        channel.Complete();

        List<AgentEvent> received = [];
        await foreach (
            AgentEvent agentEvent in channel.ReadAllAsync(TestContext.Current.CancellationToken)
        )
        {
            received.Add(agentEvent);
        }

        Assert.Collection(
            received,
            agentEvent => Assert.Same(started, agentEvent),
            agentEvent => Assert.Same(text, agentEvent),
            agentEvent => Assert.Same(finished, agentEvent)
        );
    }

    [Fact]
    public async Task Emit_does_not_block_when_no_reader_drains_the_channel()
    {
        const int count = 10_000;
        var channel = new AgentEventChannel();

        Task emitting = Task.Run(
            () =>
            {
                for (int i = 0; i < count; i++)
                {
                    channel.Emit(new TextMessageContent(RunId, "msg_1", "chunk"));
                }

                channel.Complete();
            },
            TestContext.Current.CancellationToken
        );

        await emitting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        int received = 0;
        await foreach (AgentEvent _ in channel.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            received++;
        }

        Assert.Equal(count, received);
    }

    [Fact]
    public async Task ReadAllAsync_completes_when_the_channel_is_completed()
    {
        var channel = new AgentEventChannel();
        channel.Complete();

        List<AgentEvent> received = [];
        await foreach (
            AgentEvent agentEvent in channel.ReadAllAsync(TestContext.Current.CancellationToken)
        )
        {
            received.Add(agentEvent);
        }

        Assert.Empty(received);
    }
}
