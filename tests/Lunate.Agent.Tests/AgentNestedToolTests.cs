using System.Text.Json;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class AgentNestedToolTests
{
    [Fact]
    public async Task A_nested_call_runs_with_parent_ids_and_leaves_history_to_the_outer_call()
    {
        ScriptedTool inner = ReadTool("nested contents", "inner");
        ScriptedTool outer = NestedCaller("outer", "inner");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "outer", LoopScripts.Args(("x", "1"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(outer, inner));

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        List<AgentEvent> nestedEvents = [.. events.Where(e => CallIdOf(e) == "call_1/1")];
        Assert.Equal(4, nestedEvents.Count);
        Assert.All(nestedEvents, e => Assert.Equal("call_1", ParentIdOf(e)));
        ToolCallStart nestedStart = Assert.Single(
            events.OfType<ToolCallStart>(),
            e => e.ToolName == "inner"
        );
        Assert.Equal("call_1/1", nestedStart.CallId);
        ToolCallResult nestedResult = Assert.Single(
            events.OfType<ToolCallResult>(),
            e => e.CallId == "call_1/1"
        );
        Assert.Equal("nested contents", nestedResult.Output);
        Assert.False(nestedResult.IsError);

        ChatMessage toolMessage = Assert.Single(
            client.Requests[1].Messages,
            message => message.Role == ChatRole.Tool
        );
        FunctionResultContent result = Assert.IsType<FunctionResultContent>(
            Assert.Single(toolMessage.Contents)
        );
        Assert.Equal("call_1", result.CallId);
        Assert.Equal("outer saw: nested contents", result.Result);
    }

    [Fact]
    public async Task The_nested_depth_cap_is_enforced()
    {
        var recurse = new ScriptedTool("recurse", "Calls itself.", """{"type":"object"}""");
        recurse.OnExecuteAsync = async (_, ctx, ct) =>
        {
            var execute =
                ctx.ExecuteToolAsync
                ?? throw new InvalidOperationException("The context has no nested executor.");
            return await execute("recurse", EmptyArgs(), ct);
        };
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "recurse", LoopScripts.Args(("x", "1"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(recurse),
            new AgentHarnessOptions { MaxNestedToolDepth = 1 }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(EventSequenceValidator.Validate(events));
        ToolCallResult nested = Assert.Single(
            events.OfType<ToolCallResult>(),
            e => e.CallId == "call_1/1"
        );
        Assert.True(nested.IsError);
        Assert.Contains("Nested tool call depth exceeded (1).", nested.Output);
        Assert.DoesNotContain(events.OfType<ToolCallStart>(), e => e.CallId == "call_1/1/1");
    }

    [Fact]
    public async Task The_programmatic_gate_refuses_model_only_tools()
    {
        var modelOnly = new ContractTool("model_only") { Exposure = ToolExposure.ModelOnly };
        ScriptedTool outer = NestedCaller("outer", "model_only");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "outer", LoopScripts.Args(("x", "1"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(outer, modelOnly));

        List<AgentEvent> events = await Run(harness);

        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.True(result.IsError);
        Assert.Contains("not callable programmatically", result.Output);
        Assert.Contains("ModelOnly", result.Output);
        Assert.False(modelOnly.Executed);
    }

    [Fact]
    public async Task A_nested_unknown_tool_reports_the_same_error_as_top_level()
    {
        ScriptedTool outer = NestedCaller("outer", "missing");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "outer", LoopScripts.Args(("x", "1"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(outer));

        List<AgentEvent> events = await Run(harness);

        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.True(result.IsError);
        Assert.Contains("Unknown tool 'missing'", result.Output);
        Assert.Contains("outer", result.Output);
    }

    [Fact]
    public async Task Nested_calls_use_the_same_approver()
    {
        ScriptedTool inner = ReadTool("contents", "inner");
        ScriptedTool outer = NestedCaller("outer", "inner");
        var approver = new NestedDenyApprover();
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "outer", LoopScripts.Args(("x", "1"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(outer, inner),
            new AgentHarnessOptions { Approver = approver }
        );

        List<AgentEvent> events = await Run(harness);

        ToolCallResult nested = Assert.Single(
            events.OfType<ToolCallResult>(),
            e => e.CallId == "call_1/1"
        );
        Assert.True(nested.IsError);
        Assert.Contains("Denied", nested.Output);
        Assert.Null(inner.ReceivedArgsRaw);
        Assert.Equal(["outer", "inner"], approver.SawTools);
    }

    [Fact]
    public async Task Cancellation_propagates_through_a_nested_call()
    {
        ScriptedTool inner = ReadTool("never", "inner");
        var nestedStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        inner.OnExecuteAsync = async (_, _, ct) =>
        {
            nestedStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new ToolResult("unreachable", IsError: false);
        };
        ScriptedTool outer = NestedCaller("outer", "inner");
        var client = new ScriptedChatClient().Enqueue(
            LoopScripts.Call("call_1", "outer", LoopScripts.Args(("x", "1"))),
            LoopScripts.ToolCalls()
        );
        var harness = new AgentHarness(client, Registry(outer, inner));
        using var cancellation = new CancellationTokenSource();

        Task<List<AgentEvent>> run = harness
            .RunAsync("go", cancellation.Token)
            .ToListAsync(TestContext.Current.CancellationToken);
        await nestedStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        cancellation.Cancel();
        List<AgentEvent> events = await run;

        Assert.Empty(EventSequenceValidator.Validate(events));
        Assert.Equal(StopReasons.Cancelled, Assert.IsType<RunFinished>(events[^1]).StopReason);
        ToolCallResult nested = Assert.Single(
            events.OfType<ToolCallResult>(),
            e => e.CallId == "call_1/1"
        );
        Assert.True(nested.IsError);
        Assert.Contains("cancelled by the user", nested.Output);
        Assert.Equal("call_1", nested.ParentToolCallId);
    }

    [Fact]
    public async Task A_tool_reporting_progress_emits_the_event()
    {
        ScriptedTool tool = ReadTool("contents");
        tool.OnExecute = (_, ctx) =>
        {
            var progress =
                ctx.Progress ?? throw new InvalidOperationException("The context has no progress.");
            progress("half way");
            return new ToolResult("contents", IsError: false);
        };
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(tool));

        List<AgentEvent> events = await Run(harness);

        ToolProgressUpdate progress = Assert.Single(events.OfType<ToolProgressUpdate>());
        Assert.Equal(events[0].RunId, progress.RunId);
        Assert.Equal("call_1", progress.CallId);
        Assert.Equal("half way", progress.Message);
    }

    [Fact]
    public async Task The_context_carries_the_run_call_and_file_queue()
    {
        var queue = new FileMutationQueue();
        ScriptedTool tool = ReadTool("contents");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(tool),
            new AgentHarnessOptions { FileMutations = queue }
        );

        List<AgentEvent> events = await Run(harness);

        ToolContext context = tool.ReceivedContext!;
        Assert.Equal(events[0].RunId, context.RunId);
        Assert.Equal("call_1", context.CallId);
        Assert.Same(queue, context.FileMutations);
        Assert.NotNull(context.ExecuteToolAsync);
        Assert.NotNull(context.Progress);
    }

    [Fact]
    public async Task Nested_call_counters_reset_per_run()
    {
        ScriptedTool inner = ReadTool("contents", "inner");
        ScriptedTool outer = NestedCaller("outer", "inner");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "outer", LoopScripts.Args(("x", "1"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop())
            .Enqueue(
                LoopScripts.Call("call_1", "outer", LoopScripts.Args(("x", "1"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(outer, inner));

        List<AgentEvent> first = await Run(harness);
        List<AgentEvent> second = await Run(harness);

        Assert.Contains(first.OfType<ToolCallStart>(), e => e.CallId == "call_1/1");
        Assert.Contains(second.OfType<ToolCallStart>(), e => e.CallId == "call_1/1");
    }

    private static ScriptedTool NestedCaller(string name, string nestedName)
    {
        var tool = new ScriptedTool(name, $"Calls {nestedName}.", """{"type":"object"}""");
        tool.OnExecuteAsync = async (_, ctx, ct) =>
        {
            var execute =
                ctx.ExecuteToolAsync
                ?? throw new InvalidOperationException("The context has no nested executor.");
            ToolResult nested = await execute(nestedName, EmptyArgs(), ct);
            return new ToolResult($"outer saw: {nested.Output}", IsError: nested.IsError);
        };
        return tool;
    }

    private static JsonElement EmptyArgs()
    {
        using JsonDocument document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static string? CallIdOf(AgentEvent agentEvent) =>
        agentEvent switch
        {
            ToolCallStart start => start.CallId,
            ToolCallArgs args => args.CallId,
            ToolCallEnd end => end.CallId,
            ToolCallResult result => result.CallId,
            _ => null,
        };

    private static string? ParentIdOf(AgentEvent agentEvent) =>
        agentEvent switch
        {
            ToolCallStart start => start.ParentToolCallId,
            ToolCallArgs args => args.ParentToolCallId,
            ToolCallEnd end => end.ParentToolCallId,
            ToolCallResult result => result.ParentToolCallId,
            _ => null,
        };

    private sealed class NestedDenyApprover : IToolApprover
    {
        public List<string> SawTools { get; } = [];

        public ValueTask<bool> ApproveAsync(ITool tool, JsonElement args, CancellationToken ct)
        {
            SawTools.Add(tool.Name);
            return ValueTask.FromResult(tool.Name != "inner");
        }
    }
}
