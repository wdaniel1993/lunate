using Lunate.Agent;

namespace Lunate.Extensibility.Testing.Tests;

public sealed class EventRecorderTests
{
    [Fact]
    public void Events_keep_emission_order_and_typed_queries_filter()
    {
        var recorder = new EventRecorder();

        recorder.Emit(new RunStarted("r1"));
        recorder.Emit(new TextMessageContent("r1", "m1", "hello"));
        recorder.Emit(new RunFinished("r1", "stop"));

        Assert.Equal(
            ["RunStarted", "TextMessageContent", "RunFinished"],
            recorder.Events.Select(agentEvent => agentEvent.GetType().Name)
        );
        Assert.Single(recorder.Of<RunStarted>());
        Assert.Equal("hello", recorder.Single<TextMessageContent>().Text);
    }

    [Fact]
    public void Single_throws_when_the_type_is_missing()
    {
        var recorder = new EventRecorder();
        recorder.Emit(new RunStarted("r1"));

        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = recorder.Single<RunFinished>();
        });
    }

    [Fact]
    public void OccurredBefore_compares_first_occurrences()
    {
        var recorder = new EventRecorder();
        recorder.Emit(new RunStarted("r1"));
        recorder.Emit(new RunFinished("r1", "stop"));

        Assert.True(recorder.OccurredBefore<RunStarted, RunFinished>());
        Assert.False(recorder.OccurredBefore<RunFinished, RunStarted>());
    }

    [Fact]
    public void OccurredBefore_is_false_when_either_type_is_absent()
    {
        var recorder = new EventRecorder();
        recorder.Emit(new RunStarted("r1"));

        Assert.False(recorder.OccurredBefore<RunStarted, RunFinished>());
        Assert.False(recorder.OccurredBefore<RunFinished, RunStarted>());
    }

    [Fact]
    public void Clear_empties_the_recorder()
    {
        var recorder = new EventRecorder();
        recorder.Emit(new RunStarted("r1"));

        recorder.Clear();

        Assert.Empty(recorder.Events);
    }
}
