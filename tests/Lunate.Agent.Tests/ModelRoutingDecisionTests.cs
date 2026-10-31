using Lunate.Ai;

namespace Lunate.Agent.Tests;

public sealed class ModelRoutingDecisionTests
{
    [Fact]
    public async Task Acting_decision_selects_the_profile_it_chose()
    {
        DecisionRun run = await RunAsync(
            DecisionMode.Enforce,
            """{"results":{"routing":{"answer":"fast","probabilities":{"default":0.1,"fast":0.9}}}}"""
        );

        Assert.Empty(run.Configured.Requests);
        Assert.Single(run.Fast.Requests);
        Assert.Contains(
            "fast response",
            string.Concat(run.Events.OfType<TextMessageContent>().Select(item => item.Text)),
            StringComparison.Ordinal
        );
        DecisionEvaluated decision = Assert.Single(run.Events.OfType<DecisionEvaluated>());
        Assert.Equal("routing", decision.QuestionId);
        Assert.Equal("fast", decision.Answer);
    }

    [Fact]
    public async Task Shadow_decision_records_its_choice_without_changing_the_configured_model()
    {
        DecisionRun run = await RunAsync(
            DecisionMode.Shadow,
            """{"results":{"routing":{"answer":"fast","probabilities":{"default":0.1,"fast":0.9}}}}"""
        );

        Assert.Single(run.Configured.Requests);
        Assert.Empty(run.Fast.Requests);
        Assert.Contains(
            "configured response",
            string.Concat(run.Events.OfType<TextMessageContent>().Select(item => item.Text)),
            StringComparison.Ordinal
        );
        Assert.Equal("fast", Assert.Single(run.Events.OfType<DecisionEvaluated>()).Answer);
    }

    [Fact]
    public async Task Off_contract_decision_falls_back_to_the_configured_model()
    {
        DecisionRun run = await RunAsync(DecisionMode.Enforce, "not json");

        Assert.Equal(2, run.Decision.Requests.Count);
        Assert.Single(run.Configured.Requests);
        Assert.Empty(run.Fast.Requests);
        Assert.Contains(
            "configured response",
            string.Concat(run.Events.OfType<TextMessageContent>().Select(item => item.Text)),
            StringComparison.Ordinal
        );
        Assert.Single(run.Events.OfType<DecisionFailed>());
    }

    private static async Task<DecisionRun> RunAsync(DecisionMode mode, string decisionResponse)
    {
        var configured = new ScriptedChatClient().Enqueue(
            LoopScripts.Text("configured response"),
            LoopScripts.Stop()
        );
        var fast = new ScriptedChatClient().Enqueue(
            LoopScripts.Text("fast response"),
            LoopScripts.Stop()
        );
        ScriptedChatClient decision = new();
        decision.Enqueue(LoopScripts.Text(decisionResponse), LoopScripts.Stop());
        if (decisionResponse == "not json")
        {
            decision.Enqueue(LoopScripts.Text(decisionResponse), LoopScripts.Stop());
        }

        var harness = new AgentHarness(
            configured,
            new ToolRegistry(),
            new AgentHarnessOptions
            {
                RoutingMode = mode,
                UsageModels = new Dictionary<string, UsageModelClient>(StringComparer.Ordinal)
                {
                    ["chat"] = Model(configured, "default"),
                    ["decision"] = Model(decision, "decision"),
                },
                RoutingProfiles = new Dictionary<string, UsageModelClient>(StringComparer.Ordinal)
                {
                    ["default"] = Model(configured, "default"),
                    ["fast"] = Model(fast, "fast"),
                },
            }
        );

        return new DecisionRun(await AgentTestSupport.Run(harness), configured, fast, decision);
    }

    private static UsageModelClient Model(ScriptedChatClient client, string profile) =>
        new(
            client,
            profile,
            "openai",
            new ModelInfo($"{profile}-model", "openai", null, 64000, SupportsTools: true)
        );

    private sealed record DecisionRun(
        List<AgentEvent> Events,
        ScriptedChatClient Configured,
        ScriptedChatClient Fast,
        ScriptedChatClient Decision
    );
}
