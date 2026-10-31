using Lunate.Ai;

namespace Lunate.Agent.Tests;

public sealed class DecisionApprovalTests
{
    [Fact]
    public async Task Malformed_decision_falls_back_to_the_ordinary_approval_flow()
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
        Assert.Single(approver.Requests);
        Assert.Equal(1, executions);
        Assert.Single(events.OfType<DecisionFailed>());
    }

    [Fact]
    public async Task Decision_allow_does_not_override_user_deny()
    {
        DecisionRun run = await RunApprovalAsync(
            """{"results":{"approval":{"answer":"approve","probabilities":{"approve":0.8,"deny":0.2}}}}""",
            DecisionMode.Enforce,
            userApproval: false,
            apiType: "native"
        );

        DecisionEvaluated decision = Assert.Single(run.Events.OfType<DecisionEvaluated>());
        Assert.Equal("decision-model", decision.ModelId);
        Assert.Equal("adapter", decision.Method);
        Assert.Equal("approve", decision.Option);
        Assert.Equal("approval", decision.QuestionId);
        Assert.Equal(decision.Option, decision.Answer);
        Assert.Equal(0.8d, decision.Probability);
        Assert.Single(run.Approver.Requests);
        Assert.False(run.Approver.Requests[0].EscalationRequested);
        Assert.Equal(0, run.Executions);
    }

    [Fact]
    public async Task Shadow_decision_does_not_veto_user_approval()
    {
        DecisionRun run = await RunApprovalAsync(
            """{"results":{"approval":{"answer":"deny","probabilities":{"approve":0.2,"deny":0.8}}}}""",
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
    public async Task Missing_decision_profile_reports_unavailable_without_chat_fallback()
    {
        var chat = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "write", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var approver = new RecordingApprover(decision: true);
        var tool = new ScriptedTool(
            "write",
            "Writes a file.",
            """{"type":"object"}""",
            ToolRisk.Write
        );
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
                },
            }
        );

        List<AgentEvent> events = await AgentTestSupport.Run(harness);

        Assert.Equal(2, chat.Requests.Count);
        Assert.Single(events.OfType<DecisionUnavailable>());
        Assert.Empty(events.OfType<DecisionEvaluated>());
        Assert.Single(approver.Requests);
    }

    [Fact]
    public async Task Contract_failure_does_not_disable_the_decision_profile_for_later_calls()
    {
        var chat = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "write", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("call_2", "write", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var decision = new ScriptedChatClient()
            .Enqueue(LoopScripts.Text("not JSON"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("still not JSON"), LoopScripts.Stop())
            .Enqueue(
                LoopScripts.Text(
                    """{"results":{"approval":{"answer":"approve","probabilities":{"approve":0.9,"deny":0.1}}}}"""
                ),
                LoopScripts.Stop()
            );
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

        Assert.Equal(3, decision.Requests.Count);
        Assert.Single(events.OfType<DecisionFailed>());
        Assert.Single(events.OfType<DecisionEvaluated>());
        Assert.Equal(2, approver.Requests.Count);
        Assert.Equal(2, executions);
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
