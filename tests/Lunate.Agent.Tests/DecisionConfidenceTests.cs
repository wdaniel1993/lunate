using System.Text.Json;
using Lunate.Ai.Decisions;
using Lunate.Ai.SystemOne;

namespace Lunate.Agent.Tests;

public sealed class DecisionConfidenceTests
{
    [Theory]
    [InlineData(0d, 1d)]
    [InlineData(0.5d, 0d)]
    [InlineData(1d, 1d)]
    public async Task Noul_confidence_is_distance_from_half(double yesProbability, double expected)
    {
        string response = JsonSerializer.Serialize(
            new
            {
                model = "jev-1.14",
                answers = new Dictionary<string, object>
                {
                    ["q"] = new { type = "noul", noul = yesProbability },
                },
            }
        );
        using SystemOneRecordingHandler handler = new(response);
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = new(
            httpClient,
            new SystemOnePreset(new Uri("https://example.test/api/v1/systemone"))
        );

        DecisionResult result = await client.GetDecisionAsync(
            CreateRequest(DecisionQuestionKind.Noul, ["yes", "no"]),
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal(expected, result.Results["q"].Confidence, 12);
    }

    [Fact]
    public async Task Choice_confidence_matches_the_three_option_example()
    {
        using SystemOneRecordingHandler handler = new(
            """{"model":"jev-1.14","answers":{"q":{"type":"choice","choice":"large","probabilities":{"small":0.1,"medium":0.3,"large":0.6}}}}"""
        );
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = new(
            httpClient,
            new SystemOnePreset(new Uri("https://example.test/api/v1/systemone"))
        );

        DecisionResult result = await client.GetDecisionAsync(
            CreateRequest(DecisionQuestionKind.Choice, ["small", "medium", "large"]),
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal(0.4, result.Results["q"].Confidence, 12);
    }

    [Fact]
    public async Task Adapter_and_native_confidence_match_for_the_same_distribution()
    {
        DecisionRequest request = CreateRequest(DecisionQuestionKind.Noul, ["yes", "no"]);
        using SystemOneRecordingHandler handler = new(
            """{"model":"jev-1.14","answers":{"q":{"type":"noul","noul":0.8}}}"""
        );
        using HttpClient httpClient = new(handler);
        DecisionResult native = await new SystemOneDecisionClient(
                httpClient,
                new SystemOnePreset(new Uri("https://example.test/api/v1/systemone"))
            )
            .GetDecisionAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        ScriptedChatClient chat = new ScriptedChatClient().Enqueue(
            LoopScripts.Text("""{"results":{"q":{"answer":"yes","probabilities":{"yes":0.8,"no":0.2}}}}""")
        );
        DecisionResult adapter = await new ChatDecisionClient(chat).GetDecisionAsync(
            request,
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal(native.Results["q"].Confidence, adapter.Results["q"].Confidence, 12);
    }

    private static DecisionRequest CreateRequest(
        DecisionQuestionKind kind,
        IReadOnlyList<string> answers
    )
    {
        using JsonDocument state = JsonDocument.Parse("{}");
        return new DecisionRequest(
            "typesafe/jev-1.14",
            state.RootElement.Clone(),
            [new DecisionQuestion("q", "Evaluate this question.", kind, answers)]
        );
    }
}
