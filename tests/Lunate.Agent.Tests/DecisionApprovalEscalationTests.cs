using Lunate.Ai;

namespace Lunate.Agent.Tests;

public sealed class DecisionApprovalEscalationTests
{
    private const string DenyResponse =
        """{"results":{"approval":{"answer":"deny","probabilities":{"approve":0.13,"deny":0.87}}}}""";

    [Fact]
    public async Task Deny_escalation_calls_a_prompt_capable_approver_even_when_it_ignores_the_flag()
    {
        DecisionRun run = await RunAsync(new RecordingApprover(decision: true));

        DecisionEvaluated decision = Assert.Single(run.Events.OfType<DecisionEvaluated>());
        RecordingApprover approver = Assert.IsType<RecordingApprover>(run.Approver);
        ApprovalRequest request = Assert.Single(approver.PromptRequests);
        Assert.Empty(approver.Requests);
        Assert.True(request.EscalationRequested);
        Assert.Equal("deny", request.DecisionRecommendation);
        Assert.Equal(decision.Probability, request.DecisionProbability);
        Assert.Equal("decision-model", request.DecisionModelId);
        Assert.Equal("adapter", request.DecisionMethod);
        Assert.Equal(1, run.Executions);
    }

    [Fact]
    public async Task Deny_escalation_fails_closed_with_notice_when_no_user_is_present()
    {
        var approver = new SilentApprover();
        DecisionRun run = await RunAsync(approver);

        Assert.Equal(0, approver.Calls);
        Assert.Equal(0, run.Executions);
        Assert.Contains(
            run.Events.OfType<ToolCallResult>(),
            result =>
                result.Output.Contains("decision escalated", StringComparison.OrdinalIgnoreCase)
                && result.Output.Contains("no user was present", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task Escalation_is_scoped_to_the_call_that_was_denied()
    {
        DecisionRun run = await RunAsync(
            new RecordingApprover(decision: true),
            secondDecision: """{"results":{"approval":{"answer":"approve","probabilities":{"approve":0.91,"deny":0.09}}}}"""
        );

        RecordingApprover approver = Assert.IsType<RecordingApprover>(run.Approver);
        Assert.True(Assert.Single(approver.PromptRequests).EscalationRequested);
        Assert.False(Assert.Single(approver.Requests).EscalationRequested);
        Assert.Equal(2, run.Executions);
    }

    [Fact]
    public async Task A_deny_below_the_threshold_runs_the_ordinary_approval_flow()
    {
        DecisionRun run = await RunAsync(new RecordingApprover(decision: true), threshold: 0.9);

        Assert.Empty(Assert.IsType<RecordingApprover>(run.Approver).PromptRequests);
        Assert.Equal(1, run.Executions);
    }

    [Fact]
    public async Task A_deny_that_reaches_the_threshold_escalates()
    {
        DecisionRun run = await RunAsync(new RecordingApprover(decision: true), threshold: 0.87);

        Assert.True(Assert.Single(Assert.IsType<RecordingApprover>(run.Approver).PromptRequests).EscalationRequested);
        Assert.Equal(1, run.Executions);
    }

    [Fact]
    public async Task A_deny_above_a_default_threshold_escalates()
    {
        DecisionRun run = await RunAsync(new RecordingApprover(decision: true));

        Assert.True(Assert.Single(Assert.IsType<RecordingApprover>(run.Approver).PromptRequests).EscalationRequested);
        Assert.Equal(1, run.Executions);
    }

    private static async Task<DecisionRun> RunAsync(
        IToolApprover approver,
        string? secondDecision = null,
        double? threshold = null
    )
    {
        var chat = new ScriptedChatClient().Enqueue(
            LoopScripts.Call("call_1", "write", LoopScripts.Args()),
            LoopScripts.ToolCalls()
        );
        var decision = new ScriptedChatClient().Enqueue(
            LoopScripts.Text(DenyResponse),
            LoopScripts.Stop()
        );
        if (secondDecision is not null)
        {
            chat.Enqueue(
                LoopScripts.Call("call_2", "write", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            );
            decision.Enqueue(LoopScripts.Text(secondDecision), LoopScripts.Stop());
        }

        chat.Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
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
                ApprovalsThreshold = threshold is { } value ? value : 0.5,
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

        return new DecisionRun(
            await AgentTestSupport.Run(harness),
            approver as RecordingApprover,
            executions
        );
    }

    private sealed class SilentApprover : IToolApprover
    {
        public int Calls { get; private set; }

        public ValueTask<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct)
        {
            Calls++;
            return ValueTask.FromResult(true);
        }
    }

    private sealed record DecisionRun(
        List<AgentEvent> Events,
        RecordingApprover? Approver,
        int Executions
    );
}
