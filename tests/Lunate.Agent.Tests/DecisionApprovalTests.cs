using Lunate.Ai;

namespace Lunate.Agent.Tests;

public sealed class DecisionApprovalTests
{
    [Fact]
    public async Task Malformed_decision_is_retried_once_then_fails_closed()
    {
        var chat = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "write", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var decision = new ScriptedChatClient()
            .Enqueue(LoopScripts.Text("not json"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("still not json"), LoopScripts.Stop());
        var approver = new RecordingApprover(decision: true);
        int executions = 0;
        var tool = new ScriptedTool(
            "write",
            "Writes a file.",
            """{"type":"object"}""",
            ToolRisk.Write
        );
        tool.OnExecuteAsync = (_, _, _) =>
        {
            executions++;
            return Task.FromResult(new ToolResult("written", IsError: false));
        };
        var registry = new ToolRegistry();
        registry.Add(tool);
        var harness = new AgentHarness(
            chat,
            registry,
            new AgentHarnessOptions
            {
                DecisionMode = DecisionMode.Enforce,
                Approver = approver,
                UsageModels = new Dictionary<string, UsageModelClient>(StringComparer.Ordinal)
                {
                    ["chat"] = new(
                        chat,
                        "chat-profile",
                        "openai",
                        new ModelInfo("chat-model", "openai", null, 64000, SupportsTools: true)
                    ),
                    ["decision"] = new(
                        decision,
                        "decision-profile",
                        "openai",
                        new ModelInfo("decision-model", "openai", null, 64000, SupportsTools: false)
                    ),
                },
            }
        );

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Equal(2, decision.Requests.Count);
        Assert.Empty(approver.Requests);
        Assert.Equal(0, executions);
        Assert.Contains(
            events.OfType<ToolCallResult>(),
            result =>
                result.Output.Contains("decision-model", StringComparison.Ordinal)
                && result.Output.Contains("failed", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task Decision_allow_does_not_override_user_deny()
    {
        DecisionRun run = await RunApprovalAsync(
            """{"option":"approve","probabilities":{"approve":0.8,"deny":0.2}}""",
            DecisionMode.Enforce,
            userApproval: false,
            apiType: "native"
        );

        DecisionEvaluated decision = Assert.Single(run.Events.OfType<DecisionEvaluated>());
        Assert.Equal("decision-model", decision.ModelId);
        Assert.Equal("native", decision.Method);
        Assert.Equal("approve", decision.Option);
        Assert.Equal(0.8d, decision.Probability);
        Assert.Single(run.Approver.Requests);
        Assert.Equal(0, run.Executions);
    }

    [Fact]
    public async Task Shadow_decision_does_not_veto_user_approval()
    {
        DecisionRun run = await RunApprovalAsync(
            """{"option":"deny","probabilities":{"approve":0.2,"deny":0.8}}""",
            DecisionMode.Shadow,
            userApproval: true
        );

        DecisionEvaluated decision = Assert.Single(run.Events.OfType<DecisionEvaluated>());
        Assert.Equal("adapter", decision.Method);
        Assert.Equal("deny", decision.Option);
        Assert.Single(run.Approver.Requests);
        Assert.Equal(1, run.Executions);
    }

    [Fact]
    public async Task Enforced_decision_can_tighten_a_user_approval()
    {
        DecisionRun run = await RunApprovalAsync(
            """{"option":"deny","probabilities":{"approve":0.2,"deny":0.8}}""",
            DecisionMode.Enforce,
            userApproval: true
        );

        Assert.Single(run.Events.OfType<DecisionEvaluated>());
        Assert.Empty(run.Approver.Requests);
        Assert.Equal(0, run.Executions);
    }

    private static async Task<DecisionRun> RunApprovalAsync(
        string response,
        DecisionMode mode,
        bool userApproval,
        string apiType = "adapter"
    )
    {
        var chat = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "write", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var decision = new ScriptedChatClient().Enqueue(
            LoopScripts.Text(response),
            LoopScripts.Stop()
        );
        var approver = new RecordingApprover(userApproval);
        int executions = 0;
        var tool = new ScriptedTool(
            "write",
            "Writes a file.",
            """{"type":"object"}""",
            ToolRisk.Write
        );
        tool.OnExecuteAsync = (_, _, _) =>
        {
            executions++;
            return Task.FromResult(new ToolResult("written", IsError: false));
        };
        var registry = new ToolRegistry();
        registry.Add(tool);
        var harness = new AgentHarness(
            chat,
            registry,
            new AgentHarnessOptions
            {
                DecisionMode = mode,
                Approver = approver,
                UsageModels = new Dictionary<string, UsageModelClient>(StringComparer.Ordinal)
                {
                    ["chat"] = new(
                        chat,
                        "chat-profile",
                        "openai",
                        new ModelInfo("chat-model", "openai", null, 64000, SupportsTools: true)
                    ),
                    ["decision"] = new(
                        decision,
                        "decision-profile",
                        "openai",
                        new ModelInfo("decision-model", "openai", null, 64000, SupportsTools: false)
                        {
                            ApiType = apiType,
                        }
                    ),
                },
            }
        );

        return new DecisionRun(await AgentTestSupport.Run(harness), decision, approver, executions);
    }

    private sealed record DecisionRun(
        List<AgentEvent> Events,
        ScriptedChatClient Client,
        RecordingApprover Approver,
        int Executions
    );
}
