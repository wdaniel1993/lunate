using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

/// <summary>Tool approvals of a child run route through the parent's approver with the asking-run context.</summary>
public sealed class SubagentApprovalTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static ScriptedTool SpawnTool()
    {
        var tool = new ScriptedTool(
            "spawn",
            "Spawns a subagent.",
            """{"type":"object","properties":{"prompt":{"type":"string"}}}""",
            ToolRisk.Write
        );
        tool.OnExecuteAsync = async (_, ctx, ct) =>
        {
            SubagentResult result = await ctx.Subagents!.RunAsync(new SubagentRequest("child"), ct);
            return new ToolResult(result.Output, result.IsError);
        };
        return tool;
    }

    [Fact]
    public async Task A_childs_ask_carries_the_child_and_parent_run_ids_and_the_depth()
    {
        ScriptedTool read = ReadTool("contents");
        ScriptedTool spawn = SpawnTool();
        var approver = new RecordingApprover(decision: true);
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("child done"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(read, spawn),
            new AgentHarnessOptions { Approver = approver }
        );

        List<AgentEvent> events = await Run(harness);

        string parentRunId = events.OfType<RunStarted>().First().RunId;
        string childRunId = events
            .OfType<RunStarted>()
            .Single(start => start.RunId != parentRunId)
            .RunId;
        // The spawn call itself is approved too (same seam, asking run = the parent).
        Assert.Contains(
            approver.Requests,
            request =>
                request.Tool.Name == "spawn" && request.RunId == parentRunId && request.Depth == 0
        );
        ApprovalRequest request = Assert.Single(
            approver.Requests,
            request => request.Tool.Name == "read"
        );
        Assert.Same(read, request.Tool);
        Assert.Equal(childRunId, request.RunId);
        Assert.Equal(parentRunId, request.ParentRunId);
        Assert.Equal(1, request.Depth);
        Assert.NotNull(read.ReceivedContext);
        Assert.Contains(
            events.OfType<ToolCallResult>(),
            result => result.RunId == parentRunId && !result.IsError
        );
    }

    [Fact]
    public async Task A_denied_child_call_becomes_an_error_result_the_child_can_read()
    {
        ScriptedTool read = ReadTool("contents");
        ScriptedTool spawn = SpawnTool();
        var approver = new DenyTool(denied: "read");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("child done"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(read, spawn),
            new AgentHarnessOptions { Approver = approver }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.Null(read.ReceivedContext);
        string childRunId = events
            .OfType<RunStarted>()
            .Single(start => start.ParentRunId is not null)
            .RunId;
        Assert.Contains(
            events.OfType<ToolCallResult>(),
            result =>
                result.IsError
                && result.RunId == childRunId
                && result.Output.Contains("Denied", StringComparison.Ordinal)
        );
        ApprovalRequest request = Assert.Single(approver.Requests, ask => ask.Tool.Name == "read");
        Assert.Equal(1, request.Depth);
        Assert.Equal(childRunId, request.RunId);
    }

    /// <summary>Allows every tool except one, recording every ask.</summary>
    private sealed class DenyTool(string denied) : IToolApprover
    {
        public List<ApprovalRequest> Requests { get; } = [];

        public ValueTask<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return ValueTask.FromResult(request.Tool.Name != denied);
        }
    }
}
