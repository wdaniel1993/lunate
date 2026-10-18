using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class AgentThinkingLevelTests
{
    [Fact]
    public async Task An_effort_model_carries_the_level_as_the_reasoning_effort()
    {
        (_, ScriptedChatClient client) = await RunAsync(style: "effort", ThinkingLevel.High);

        Assert.Equal(ReasoningEffort.High, client.Requests[0].Options!.Reasoning?.Effort);
    }

    [Fact]
    public async Task A_budget_model_carries_extended_thinking_and_a_max_tokens_above_the_budget()
    {
        (_, ScriptedChatClient client) = await RunAsync(style: "budget", ThinkingLevel.High);

        ChatOptions options = client.Requests[0].Options!;
        Assert.NotNull(options.RawRepresentationFactory);
        Assert.True(options.MaxOutputTokens > 32_768);
    }

    [Fact]
    public async Task An_inline_model_carries_no_provider_parameter()
    {
        (_, ScriptedChatClient client) = await RunAsync(style: "inline", ThinkingLevel.High);

        ChatOptions options = client.Requests[0].Options!;
        Assert.Null(options.Reasoning);
        Assert.Null(options.RawRepresentationFactory);
        Assert.Null(options.MaxOutputTokens);
    }

    [Fact]
    public async Task Level_off_sends_nothing_for_a_declared_style()
    {
        (_, ScriptedChatClient client) = await RunAsync(style: "effort", ThinkingLevel.Off);

        ChatOptions options = client.Requests[0].Options!;
        Assert.Null(options.Reasoning);
        Assert.Null(options.RawRepresentationFactory);
    }

    [Fact]
    public async Task A_model_without_a_declared_style_sends_nothing()
    {
        (_, ScriptedChatClient client) = await RunAsync(style: null, ThinkingLevel.High);

        ChatOptions options = client.Requests[0].Options!;
        Assert.Null(options.Reasoning);
        Assert.Null(options.RawRepresentationFactory);
    }

    private static async Task<(AgentHarness Harness, ScriptedChatClient Client)> RunAsync(
        string? style,
        ThinkingLevel level
    )
    {
        var client = new ScriptedChatClient().Enqueue(LoopScripts.Stop());
        var options = new AgentHarnessOptions
        {
            ModelCatalog = TestCatalog.WithReasoning(style),
            ModelId = TestCatalog.ModelId,
            ThinkingLevel = level,
        };
        var harness = new AgentHarness(client, new ToolRegistry(), options);

        await harness
            .RunAsync("Hi", TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        return (harness, client);
    }
}
