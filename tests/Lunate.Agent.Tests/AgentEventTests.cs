using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class AgentEventTests
{
    private const string RunId = "run_1";

    public static TheoryData<AgentEvent> AllEvents() =>
        new()
        {
            new RunStarted(RunId),
            new RunFinished(RunId, "end_turn"),
            new RunError(RunId, "provider failed"),
            new TextMessageStart(RunId, "msg_1"),
            new TextMessageContent(RunId, "msg_1", "hello"),
            new TextMessageEnd(RunId, "msg_1"),
            new ToolCallStart(RunId, "call_1", "read"),
            new ToolCallArgs(RunId, "call_1", """{"path":"a.txt"}"""),
            new ToolCallEnd(RunId, "call_1"),
            new ToolCallResult(RunId, "call_1", "1  hello", IsError: false, Details: "diff"),
            new ApprovalRequested(RunId, "call_1", "bash", """{"command":"ls"}"""),
            new UsageUpdated(RunId, new UsageDetails { InputTokenCount = 12, OutputTokenCount = 3 }),
            new Retrying(RunId, 2, "rate limited"),
            new CompactionApplied(RunId),
            new StepLimitReached(RunId, 50),
        };

    public static TheoryData<ExtensionEvent> ExtensionEvents() =>
        new()
        {
            new ApprovalRequested(RunId, "call_1", "bash", "{}"),
            new UsageUpdated(RunId, new UsageDetails()),
            new Retrying(RunId, 1, "boom"),
            new CompactionApplied(RunId),
            new StepLimitReached(RunId, 50),
        };

    [Theory]
    [MemberData(nameof(AllEvents))]
    public void Every_event_carries_the_run_id(AgentEvent agentEvent) =>
        Assert.Equal(RunId, agentEvent.RunId);

    [Theory]
    [MemberData(nameof(ExtensionEvents))]
    public void Extension_events_are_identifiable_as_extensions(ExtensionEvent extensionEvent) =>
        Assert.IsAssignableFrom<AgentEvent>(extensionEvent);

    [Fact]
    public void Core_events_are_not_extensions()
    {
        AgentEvent started = new RunStarted(RunId);
        AgentEvent result = new ToolCallResult(RunId, "call_1", "out", IsError: false);

        Assert.False(started is ExtensionEvent);
        Assert.False(result is ExtensionEvent);
    }

    [Fact]
    public void Every_concrete_event_type_is_sealed()
    {
        Type[] concreteTypes = [.. typeof(AgentEvent).Assembly
            .GetTypes()
            .Where(type => typeof(AgentEvent).IsAssignableFrom(type) && !type.IsAbstract)];

        Assert.Equal(15, concreteTypes.Length);
        Assert.All(concreteTypes, type => Assert.True(type.IsSealed, $"{type.Name} must be sealed"));
    }

    [Fact]
    public void The_event_set_is_closed_by_concrete_types_only()
    {
        Assert.True(typeof(AgentEvent).IsAbstract);
        Assert.DoesNotContain(
            typeof(AgentEvent).Assembly.GetTypes(),
            type => typeof(AgentEvent).IsAssignableFrom(type) && type.IsInterface);
    }

    [Fact]
    public void Run_events_expose_their_fields()
    {
        var finished = new RunFinished(RunId, "end_turn");
        var error = new RunError(RunId, "provider failed");

        Assert.Equal("end_turn", finished.StopReason);
        Assert.Equal("provider failed", error.Message);
    }

    [Fact]
    public void Text_events_expose_message_ids_and_text()
    {
        var start = new TextMessageStart(RunId, "msg_1");
        var content = new TextMessageContent(RunId, "msg_1", "hello");
        var end = new TextMessageEnd(RunId, "msg_1");

        Assert.Equal("msg_1", start.MessageId);
        Assert.Equal(("msg_1", "hello"), (content.MessageId, content.Text));
        Assert.Equal("msg_1", end.MessageId);
    }

    [Fact]
    public void Tool_events_expose_call_ids_names_and_results()
    {
        var start = new ToolCallStart(RunId, "call_1", "read");
        var args = new ToolCallArgs(RunId, "call_1", """{"path":"a.txt"}""");
        var end = new ToolCallEnd(RunId, "call_1");
        var result = new ToolCallResult(RunId, "call_1", "1  hello", IsError: false, Details: "diff");

        Assert.Equal(("call_1", "read"), (start.CallId, start.ToolName));
        Assert.Equal(("call_1", """{"path":"a.txt"}"""), (args.CallId, args.Args));
        Assert.Equal("call_1", end.CallId);
        Assert.Equal(
            ("call_1", "1  hello", false, "diff"),
            (result.CallId, result.Output, result.IsError, result.Details));
    }

    [Fact]
    public void Extension_events_expose_their_fields()
    {
        var approval = new ApprovalRequested(RunId, "call_1", "bash", """{"command":"ls"}""");
        var usage = new UsageUpdated(RunId, new UsageDetails { InputTokenCount = 12, OutputTokenCount = 3 });
        var retrying = new Retrying(RunId, 2, "rate limited");
        var stepLimit = new StepLimitReached(RunId, 50);

        Assert.Equal(("call_1", "bash", """{"command":"ls"}"""), (approval.CallId, approval.ToolName, approval.Args));
        Assert.Equal(12L, usage.Usage.InputTokenCount);
        Assert.Equal(3L, usage.Usage.OutputTokenCount);
        Assert.Equal((2, "rate limited"), (retrying.Attempt, retrying.Reason));
        Assert.Equal(50, stepLimit.MaxSteps);
        Assert.Equal(RunId, new CompactionApplied(RunId).RunId);
    }
}
