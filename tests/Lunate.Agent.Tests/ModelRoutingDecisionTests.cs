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
        UsageUpdated decisionUsage = Assert.Single(
            run.Events.OfType<UsageUpdated>(),
            item => item.Attribution?.Usage == "decision"
        );
        Assert.Equal("decision", decisionUsage.Attribution?.Usage);
        Assert.Equal("decision", decisionUsage.Attribution?.ProfileName);
        Assert.Equal("adapter", decisionUsage.Attribution?.DecisionMethod);
    }

    [Fact]
    public async Task Acting_decision_routes_to_the_most_probable_profile()
    {
        DecisionRun run = await RunAsync(
            DecisionMode.Enforce,
            """{"results":{"routing":{"answer":"default","probabilities":{"default":0.1,"fast":0.9}}}}"""
        );

        Assert.Empty(run.Configured.Requests);
        Assert.Single(run.Fast.Requests);
        Assert.Equal("fast", Assert.Single(run.Events.OfType<DecisionEvaluated>()).Answer);
    }

    [Fact]
    public async Task Low_confidence_decision_keeps_the_configured_profile()
    {
        DecisionRun run = await RunAsync(
            DecisionMode.Enforce,
            """{"results":{"routing":{"answer":"fast","probabilities":{"default":0.3,"fast":0.7}}}}"""
        );

        Assert.Single(run.Configured.Requests);
        Assert.Empty(run.Fast.Requests);
        DecisionEvaluated decision = Assert.Single(run.Events.OfType<DecisionEvaluated>());
        Assert.Equal("confidence-below-threshold", decision.Outcome);
        Assert.Equal(0.4, decision.Confidence!.Value, precision: 12);
        Assert.Equal(0.5, decision.ConfidenceThreshold);
    }

    [Fact]
    public async Task Confidence_at_the_threshold_routes_to_the_winning_profile()
    {
        DecisionRun run = await RunAsync(
            DecisionMode.Enforce,
            """{"results":{"routing":{"answer":"fast","probabilities":{"default":0.25,"fast":0.75}}}}"""
        );

        Assert.Empty(run.Configured.Requests);
        Assert.Single(run.Fast.Requests);
        DecisionEvaluated decision = Assert.Single(run.Events.OfType<DecisionEvaluated>());
        Assert.Equal("routed", decision.Outcome);
        Assert.Equal(0.5, decision.Confidence);
        Assert.Equal(0.5, decision.ConfidenceThreshold);
        Assert.Collection(
            decision.Distribution!,
            item =>
            {
                Assert.Equal("default", item.Answer);
                Assert.Equal(0.25, item.Probability);
            },
            item =>
            {
                Assert.Equal("fast", item.Answer);
                Assert.Equal(0.75, item.Probability);
            }
        );
    }

    [Fact]
    public async Task Configured_routing_threshold_controls_the_confidence_gate()
    {
        DecisionRun run = await RunAsync(
            DecisionMode.Enforce,
            """{"results":{"routing":{"answer":"fast","probabilities":{"default":0.1,"fast":0.9}}}}""",
            routingMinConfidence: 0.9
        );

        Assert.Single(run.Configured.Requests);
        Assert.Empty(run.Fast.Requests);
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

    private static async Task<DecisionRun> RunAsync(
        DecisionMode mode,
        string decisionResponse,
        double routingMinConfidence = 0.5
    )
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
        decision.Enqueue(
            LoopScripts.Text(decisionResponse),
            LoopScripts.Usage(500, 20),
            LoopScripts.Stop()
        );
        if (decisionResponse == "not json")
        {
            decision.Enqueue(
                LoopScripts.Text(decisionResponse),
                LoopScripts.Usage(500, 20),
                LoopScripts.Stop()
            );
        }

        var harness = new AgentHarness(
            configured,
            new ToolRegistry(),
            new AgentHarnessOptions
            {
                RoutingMode = mode,
                RoutingMinConfidence = routingMinConfidence,
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
