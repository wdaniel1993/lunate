using System.Text.Json;
using Lunate.Ai.Decisions;
using Lunate.Ai.SystemOne;

namespace Lunate.Agent.Tests;

public sealed class SystemOneDecisionMappingTests
{
    [Theory]
    [InlineData("\"plain text\"")]
    [InlineData("[\"first\",\"second\"]")]
    public async Task State_json_is_passed_through_without_restructuring(string stateJson)
    {
        using SystemOneRecordingHandler handler = new(
            """{"model":"typesafe/jev-1.14","answers":{"safe":{"type":"noul","noul":0.91}}}"""
        );
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = CreateClient(httpClient);
        using JsonDocument state = JsonDocument.Parse(stateJson);

        await client.GetDecisionAsync(
            SystemOneDecisionTestData.CreateRequest(state.RootElement.Clone()),
            cancellationToken: TestContext.Current.CancellationToken
        );

        using JsonDocument sent = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(state.RootElement.GetRawText(), sent.RootElement.GetProperty("state").GetRawText());
    }

    [Fact]
    public async Task Choice_exchange_uses_an_object_criteria_and_maps_probabilities()
    {
        using SystemOneRecordingHandler handler = new(
            """{"model":"typesafe/jev-1.14","answers":{"team":{"type":"choice","choice":"payments","probabilities":{"payments":0.77,"frontend":0.23}}}}"""
        );
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = CreateClient(httpClient);
        DecisionQuestion question = new(
            "team",
            "Which team owns the change?",
            DecisionQuestionKind.Choice,
            ["payments", "frontend"],
            new Dictionary<string, string>
            {
                ["payments"] = "Payment processing changes.",
                ["frontend"] = "User interface changes.",
            }
        );

        DecisionResult result = await client.GetDecisionAsync(
            CreateRequest(question),
            cancellationToken: TestContext.Current.CancellationToken
        );

        using JsonDocument sent = JsonDocument.Parse(handler.RequestBody!);
        JsonElement criteria = sent
            .RootElement.GetProperty("questions")
            .GetProperty("team")
            .GetProperty("criteria");
        Assert.Equal(JsonValueKind.Object, criteria.ValueKind);
        Assert.Equal("Payment processing changes.", criteria.GetProperty("payments").GetString());
        Assert.Equal("payments", result.Results["team"].Answer);
        Assert.Equal(0.77, result.Results["team"].Probability);
        Assert.Equal(0.54, result.Results["team"].Confidence, 10);
    }

    [Fact]
    public async Task Score_exchange_uses_ordered_array_criteria_and_legend_distribution()
    {
        using SystemOneRecordingHandler handler = new(
            """{"model":"typesafe/jev-1.14","answers":{"urgency":{"type":"score","score":1.43,"legend":{"0":"reject","1":"weak","2":"strong"},"probabilities":{"0":0,"1":0.57,"2":0.43},"confidence":0.99}}}"""
        );
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = CreateClient(httpClient);
        DecisionQuestion question = new(
            "urgency",
            "How urgent is the issue?",
            DecisionQuestionKind.Score,
            ["reject", "weak", "strong"],
            new Dictionary<string, string>
            {
                ["reject"] = "No action is needed.",
                ["weak"] = "Address it soon.",
                ["strong"] = "Address it now.",
            }
        );

        DecisionResult result = await client.GetDecisionAsync(
            CreateRequest(question),
            cancellationToken: TestContext.Current.CancellationToken
        );

        using JsonDocument sent = JsonDocument.Parse(handler.RequestBody!);
        JsonElement criteria = sent
            .RootElement.GetProperty("questions")
            .GetProperty("urgency")
            .GetProperty("criteria");
        Assert.Equal(JsonValueKind.Array, criteria.ValueKind);
        Assert.Equal("No action is needed.", criteria[0].GetString());
        Assert.Equal("Address it now.", criteria[2].GetString());
        Assert.Equal("weak", result.Results["urgency"].Answer);
        Assert.Equal(
            ["reject", "weak", "strong"],
            result.Results["urgency"].Distribution.Select(item => item.Answer)
        );
        // The formula is exact; the spec's 0.35 example is rounded relative to its stated inputs.
        Assert.Equal(0.355, result.Results["urgency"].Confidence, 10);
        Assert.Equal(1.43, result.Results["urgency"].ExpectedScore);
    }

    [Fact]
    public async Task Box_exchange_maps_fractional_region_and_provider_confidence()
    {
        using SystemOneRecordingHandler handler = new(
            """{"model":"typesafe/jev-1.14","answers":{"logo":{"type":"box","box":{"top":0.05,"bottom":0.44,"left":0.41,"right":0.54},"confidence":0.16}}}"""
        );
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = CreateClient(httpClient);
        DecisionQuestion question = new(
            "logo",
            "Locate the logo.",
            DecisionQuestionKind.Box
        );

        DecisionResult result = await client.GetDecisionAsync(
            CreateRequest(question),
            cancellationToken: TestContext.Current.CancellationToken
        );

        using JsonDocument sent = JsonDocument.Parse(handler.RequestBody!);
        JsonElement mappedQuestion = sent
            .RootElement.GetProperty("questions")
            .GetProperty("logo");
        Assert.False(mappedQuestion.TryGetProperty("criteria", out _));
        Assert.Equal(0.16, result.Results["logo"].Confidence);
        Assert.Equal(0.05, result.Results["logo"].Box!.Top);
        Assert.Empty(result.Results["logo"].Distribution);
    }

    private static SystemOneDecisionClient CreateClient(HttpClient httpClient) =>
        new(httpClient, new SystemOnePreset(new Uri("https://example.test/api/v1/systemone")));

    private static DecisionRequest CreateRequest(DecisionQuestion question)
    {
        using JsonDocument state = JsonDocument.Parse("{}");
        return new DecisionRequest("typesafe/jev-1.14", state.RootElement.Clone(), [question]);
    }
}
