using System.Text.Json;
using Lunate.Ai.Decisions;

namespace Lunate.Agent.Tests;

public sealed class DecisionContractTests
{
    [Fact]
    [Trait("Category", "DecisionLayer")]
    public async Task Box_decision_result_exposes_geometry_and_confidence()
    {
        using JsonDocument state = JsonDocument.Parse(
            """{"parts":[{"type":"image","mimeType":"image/png","data":"AQ=="}]}"""
        );
        Lunate.Contracts.DecisionRequest request = new(
            state.RootElement.Clone(),
            [new("logo", "Locate the logo.", Lunate.Contracts.DecisionQuestionKind.Box)]
        );
        ScriptedChatClient chat = new ScriptedChatClient().Enqueue(
            LoopScripts.Text(
                """{"results":{"logo":{"box":{"top":0.1,"bottom":0.4,"left":0.2,"right":0.8},"confidence":0.75}}}"""
            )
        );

        Lunate.Contracts.DecisionResponse response = await DecisionEngine.EvaluateAsync(
            request,
            chat,
            "vision-model",
            "adapter",
            TestContext.Current.CancellationToken,
            supportsImages: true
        );
        Lunate.Contracts.DecisionAnswer answer = response.Results["logo"];

        Assert.Equal(0.75d, answer.Probability);
        Assert.Equal(0.75d, answer.Confidence, precision: 12);
        Assert.Empty(answer.Distribution);
        Assert.Equal(new Lunate.Contracts.DecisionBox(0.1, 0.4, 0.2, 0.8, 0.75), answer.Box);
    }

    [Fact]
    [Trait("Category", "DecisionLayer")]
    public async Task Image_state_is_refused_before_call_when_model_lacks_image_input()
    {
        using JsonDocument state = JsonDocument.Parse(
            """{"parts":[{"type":"image","mimeType":"image/png","data":"AQ=="}]}"""
        );
        Lunate.Contracts.DecisionRequest request = new(
            state.RootElement.Clone(),
            [new("safe", "Is this safe?", Lunate.Contracts.DecisionQuestionKind.YesNo)]
        );
        ScriptedChatClient chat = new();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DecisionEngine.EvaluateAsync(
                request,
                chat,
                "text-only-model",
                "adapter",
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("text-only-model", error.Message, StringComparison.Ordinal);
        Assert.Contains("image", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(chat.Requests);
    }

    [Fact]
    [Trait("Category", "DecisionLayer")]
    public void Box_answer_rejects_out_of_range_geometry()
    {
        using JsonDocument state = JsonDocument.Parse("{}");
        DecisionRequest request = new(
            "vision-model",
            state.RootElement.Clone(),
            [new("logo", "Locate the logo.", DecisionQuestionKind.Box)]
        );

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            DecisionResponseParser.Parse(
                """{"results":{"logo":{"box":{"top":1.2,"bottom":0.4,"left":0.2,"right":0.8},"confidence":0.75}}}""",
                request,
                "vision-model",
                "native"
            )
        );

        Assert.Contains("box answer", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "DecisionLayer")]
    public void Non_box_answer_rejects_box_geometry()
    {
        using JsonDocument state = JsonDocument.Parse("{}");
        DecisionRequest request = new(
            "decision-model",
            state.RootElement.Clone(),
            [new("allow", "Is this safe?", DecisionQuestionKind.Choice, ["yes", "no"])]
        );

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            DecisionResponseParser.Parse(
                """{"results":{"allow":{"answer":"yes","probabilities":{"yes":0.9,"no":0.1},"box":{"top":0.1,"bottom":0.4,"left":0.2,"right":0.8}}}}""",
                request,
                "decision-model",
                "native"
            )
        );

        Assert.Contains("non-box", error.Message, StringComparison.Ordinal);
    }
}
