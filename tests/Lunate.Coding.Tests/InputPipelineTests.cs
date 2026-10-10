using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

public sealed class InputPipelineTests
{
    [Fact]
    public async Task Without_a_hook_runner_the_text_passes_through()
    {
        var pipeline = new InputPipeline();

        InputPipelineResult result = await pipeline.ProcessAsync(
            "hello",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("hello", result.Text);
        Assert.False(result.Consumed);
    }

    [Fact]
    public async Task A_pass_through_handler_keeps_the_text()
    {
        var runner = new HookRunner();
        var handler = new ScriptedHandler(_ => new InputReceivedResult.PassThrough());
        runner.Register("ext.test", handler);
        var pipeline = new InputPipeline(runner);

        InputPipelineResult result = await pipeline.ProcessAsync(
            "hello",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("hello", result.Text);
        Assert.False(result.Consumed);
        Assert.Equal(["hello"], handler.Seen);
    }

    [Fact]
    public async Task A_transform_is_seen_by_the_next_handler()
    {
        var runner = new HookRunner();
        var first = new ScriptedHandler(_ => new InputReceivedResult.Transform("HELLO"));
        var second = new ScriptedHandler(_ => new InputReceivedResult.PassThrough());
        runner.Register("ext.first", first);
        runner.Register("ext.second", second);
        var pipeline = new InputPipeline(runner);

        InputPipelineResult result = await pipeline.ProcessAsync(
            "hello",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("HELLO", result.Text);
        Assert.False(result.Consumed);
        Assert.Equal(["hello"], first.Seen);
        Assert.Equal(["HELLO"], second.Seen);
    }

    [Fact]
    public async Task A_consuming_handler_stops_the_chain()
    {
        var runner = new HookRunner();
        var consumer = new ScriptedHandler(_ => new InputReceivedResult.Consume());
        var later = new ScriptedHandler(_ => new InputReceivedResult.Transform("late"));
        runner.Register("ext.first", consumer);
        runner.Register("ext.second", later);
        var pipeline = new InputPipeline(runner);

        InputPipelineResult result = await pipeline.ProcessAsync(
            "secret",
            TestContext.Current.CancellationToken
        );

        Assert.True(result.Consumed);
        Assert.Equal(["secret"], consumer.Seen);
        Assert.Empty(later.Seen);
    }

    private sealed class ScriptedHandler(Func<string, InputReceivedResult> handle)
        : IInputReceivedHandler
    {
        public int Priority => 0;

        public List<string> Seen { get; } = [];

        public ValueTask<InputReceivedResult> HandleAsync(
            InputReceivedPayload payload,
            CancellationToken cancellationToken
        )
        {
            Seen.Add(payload.Text);
            return ValueTask.FromResult(handle(payload.Text));
        }
    }
}
