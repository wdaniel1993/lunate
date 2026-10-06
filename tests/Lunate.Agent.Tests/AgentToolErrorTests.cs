using System.Text.Json;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class AgentToolErrorTests
{
    [Fact]
    public async Task An_unknown_tool_becomes_an_error_result_and_the_run_continues()
    {
        ScriptedTool read = ReadTool("contents");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "nope", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(read));

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.True(result.IsError);
        Assert.Contains("nope", result.Output);
        Assert.Contains("read", result.Output);
        Assert.Equal(StopReasons.Stop, Assert.IsType<RunFinished>(events[^1]).StopReason);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task A_tool_exception_becomes_an_error_result_and_the_run_continues()
    {
        ScriptedTool read = ReadTool("contents");
        read.OnExecute = (_, _) => throw new InvalidOperationException("disk exploded");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(read));

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.True(result.IsError);
        Assert.Contains("read", result.Output);
        Assert.Contains("disk exploded", result.Output);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task Malformed_arguments_become_an_error_result_and_the_run_continues()
    {
        var circular = new CircularNode();
        circular.Self = circular;
        ScriptedTool read = ReadTool("contents");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", circular))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(read));

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.True(result.IsError);
        Assert.Contains("read", result.Output);
        Assert.Contains("arguments", result.Output);
        Assert.Null(read.ReceivedContext);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task Malformed_arguments_from_the_pipeline_become_an_error_result()
    {
        ScriptedTool read = ReadTool("contents");
        var provider = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.CallFragment("call_1", "read", """{"path":"""),
                LoopScripts.CallFragment("call_1", string.Empty, " broken"),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(TestPipeline.Create(provider), Registry(read));

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        Assert.Equal("""{"path": broken""", Assert.Single(events.OfType<ToolCallArgs>()).Args);
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.True(result.IsError);
        Assert.Contains("read", result.Output);
        Assert.Contains("arguments", result.Output);
        Assert.Null(read.ReceivedContext);
        Assert.Equal(2, provider.Requests.Count);
    }

    [Fact]
    public async Task Fragmented_arguments_from_a_raw_client_become_an_error_result()
    {
        ScriptedTool read = ReadTool("contents");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.CallFragment("call_1", "read", """{"path":"""),
                LoopScripts.CallFragment("call_1", string.Empty, " broken"),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(read));

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        Assert.Equal("""{"path":""", Assert.Single(events.OfType<ToolCallArgs>()).Args);
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.True(result.IsError);
        Assert.Contains("read", result.Output);
        Assert.Contains("arguments", result.Output);
        Assert.Null(read.ReceivedContext);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task A_declined_call_becomes_an_error_result_and_the_tool_does_not_run()
    {
        ScriptedTool read = ReadTool("contents");
        var approver = new RecordingApprover(decision: false);
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(read),
            new AgentHarnessOptions { Approver = approver }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.True(result.IsError);
        Assert.Contains("read", result.Output);
        Assert.Null(read.ReceivedContext);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task An_approver_is_consulted_with_the_tool_and_its_arguments()
    {
        ScriptedTool read = ReadTool("contents");
        var approver = new RecordingApprover(decision: true);
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(read),
            new AgentHarnessOptions { Approver = approver }
        );

        List<AgentEvent> events = await Run(harness);

        (ITool tool, string args) = Assert.Single(approver.Calls);
        Assert.Same(read, tool);
        Assert.Equal("""{"path":"a.txt"}""", args);
        Assert.False(Assert.Single(events.OfType<ToolCallResult>()).IsError);
        Assert.NotNull(read.ReceivedContext);
    }

    [Fact]
    public async Task An_unknown_tool_is_not_sent_to_the_approver()
    {
        ScriptedTool read = ReadTool("contents");
        var approver = new RecordingApprover(decision: true);
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "nope", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(read),
            new AgentHarnessOptions { Approver = approver }
        );

        await Run(harness);

        Assert.Empty(approver.Calls);
    }

    [Fact]
    public async Task A_tool_cancelled_without_a_run_cancellation_becomes_an_error_result()
    {
        ScriptedTool read = ReadTool("contents");
        read.OnExecute = (_, _) => throw new TaskCanceledException("tool timed out");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(read));

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.True(result.IsError);
        Assert.Contains("tool timed out", result.Output);
        Assert.Empty(events.OfType<Retrying>());
        Assert.Equal(StopReasons.Stop, Assert.IsType<RunFinished>(events[^1]).StopReason);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task An_approver_exception_becomes_an_approval_error_result()
    {
        ScriptedTool read = ReadTool("contents");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(read),
            new AgentHarnessOptions
            {
                Approver = new ThrowingApprover(
                    new InvalidOperationException("approval service down")
                ),
            }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.True(result.IsError);
        Assert.Contains("Approval for tool 'read' failed", result.Output);
        Assert.Contains("approval service down", result.Output);
        Assert.Contains("The call was not run", result.Output);
        Assert.Null(read.ReceivedContext);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task A_tool_emitting_a_loop_event_is_rejected_and_becomes_an_error_result()
    {
        ScriptedTool read = ReadTool("contents");
        read.OnExecute = (_, context) =>
        {
            context.Events.Emit(new RunFinished("from_tool", StopReasons.Stop));
            return new ToolResult("never returned", IsError: false);
        };
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(read));

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.True(result.IsError);
        Assert.Contains("Tools may only emit extension events", result.Output);
        Assert.Contains("RunFinished", result.Output);
        Assert.Equal(StopReasons.Stop, Assert.IsType<RunFinished>(events[^1]).StopReason);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task A_null_tool_output_cannot_crash_the_loop()
    {
        ScriptedTool read = ReadTool("contents");
        read.OnExecute = (_, _) => new ToolResult(null!, IsError: false);
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(read));

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        Assert.Equal(string.Empty, Assert.Single(events.OfType<ToolCallResult>()).Output);
        Assert.Equal(StopReasons.Stop, Assert.IsType<RunFinished>(events[^1]).StopReason);
    }

    private sealed class ThrowingApprover(Exception failure) : IToolApprover
    {
        public ValueTask<bool> ApproveAsync(ITool tool, JsonElement args, CancellationToken ct) =>
            throw failure;
    }

    private sealed class CircularNode
    {
        public CircularNode? Self { get; set; }
    }
}
