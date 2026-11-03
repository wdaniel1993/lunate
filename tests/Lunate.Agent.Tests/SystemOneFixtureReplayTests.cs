using System.Net;
using System.Text.Json;
using Lunate.Ai.Decisions;
using Lunate.Ai.SystemOne;

namespace Lunate.Agent.Tests;

public sealed class SystemOneFixtureReplayTests
{
    [Fact]
    public async Task OpenRouter_fixture_replays_noul_choice_and_score_answers()
    {
        using SystemOneRecordingHandler handler = new(ReadFixture("openrouter-noul-choice-score.json"));
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = CreateClient(httpClient);
        DecisionRequest request = CreateRequest(
            new DecisionQuestion(
                "is_bug",
                "Is this a bug?",
                DecisionQuestionKind.Noul,
                ["yes", "no"]
            ),
            new DecisionQuestion(
                "team",
                "Which team should handle it?",
                DecisionQuestionKind.Choice,
                ["payments", "frontend"]
            ),
            new DecisionQuestion(
                "urgency",
                "How urgent is it?",
                DecisionQuestionKind.Score,
                ["can wait", "this week", "blocking revenue"]
            )
        );

        DecisionResult result = await client.GetDecisionAsync(
            request,
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal("yes", result.Results["is_bug"].Answer);
        Assert.Equal("typesafe/jev-1.14-20261014", result.Results["is_bug"].ModelId);
        Assert.Equal("payments", result.Results["team"].Answer);
        Assert.Equal("blocking revenue", result.Results["urgency"].Answer);
        Assert.Equal(2, result.Results["urgency"].ExpectedScore);
        Assert.Equal(394, result.Usage!.InputTokens);
        Assert.Equal(63, result.Usage.OutputTokens);
        Assert.Equal(0.000016548, result.Usage.Cost);
        Assert.True(result.Usage.IsProviderReported);
    }

    [Fact]
    public async Task OpenRouter_fixture_replays_a_box_answer()
    {
        using SystemOneRecordingHandler handler = new(ReadFixture("openrouter-box.json"));
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = CreateClient(httpClient);
        using JsonDocument state = JsonDocument.Parse(
            """{"parts":[{"type":"image","mimeType":"image/png","data":"AQ=="}]}"""
        );

        DecisionResult result = await client.GetDecisionAsync(
            CreateRequest(
                new DecisionQuestion("logo", "Locate the logo.", DecisionQuestionKind.Box),
                state.RootElement.Clone()
            ),
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal(0.16, result.Results["logo"].Confidence);
        DecisionBox box = result.Results["logo"].Box!;
        Assert.Equal(0.05, box.Top);
        Assert.Equal(0.44, box.Bottom);
    }

    [Fact]
    public async Task OpenCode_fixture_replays_noul_and_usage()
    {
        using SystemOneRecordingHandler handler = new(ReadFixture("opencode-jev113-noul.json"));
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = CreateClient(httpClient);

        DecisionResult result = await client.GetDecisionAsync(
            CreateRequest(new DecisionQuestion("q", "Is this true?", DecisionQuestionKind.Noul, ["yes", "no"])),
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal("no", result.Results["q"].Answer);
        Assert.Equal(0.43, result.Results["q"].Distribution[0].Probability);
        Assert.Equal(270, result.Usage!.InputTokens);
        Assert.Equal(20, result.Usage.OutputTokens);
        Assert.Equal(0.00001134, result.Usage.Cost);
    }

    [Theory]
    [InlineData("openrouter-unknown-model.json", "does not exist")]
    [InlineData("openrouter-unknown-question-type.json", "Invalid discriminator")]
    [InlineData("openrouter-score-criteria-array-required.json", "expected array")]
    [InlineData("openrouter-choice-criteria-object-required.json", "expected record")]
    [InlineData("opencode-images-need-header.json", "TypeSafe-Danger-Zone-Images")]
    [InlineData("opencode-jev114-unavailable.json", "Model is unavailable")]
    public async Task Provider_error_fixture_is_reported_without_retry(
        string fileName,
        string expectedMessage
    )
    {
        using SystemOneRecordingHandler handler = new(ReadFixture(fileName), HttpStatusCode.BadRequest);
        using HttpClient httpClient = new(handler);
        SystemOneDecisionClient client = CreateClient(httpClient);

        DecisionTransportException exception = await Assert.ThrowsAsync<DecisionTransportException>(
            () => client.GetDecisionAsync(
                SystemOneDecisionTestData.CreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken
            )
        );

        Assert.Contains(expectedMessage, exception.ProviderMessage, StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);
    }

    private static SystemOneDecisionClient CreateClient(HttpClient httpClient) =>
        new(httpClient, new SystemOnePreset(new Uri("https://example.test/api/v1/systemone")));

    private static DecisionRequest CreateRequest(params DecisionQuestion[] questions) =>
        CreateRequest(questions, JsonDocument.Parse("{}").RootElement.Clone());

    private static DecisionRequest CreateRequest(DecisionQuestion question, JsonElement state) =>
        new("typesafe/jev-1.14", state, [question]);

    private static DecisionRequest CreateRequest(DecisionQuestion[] questions, JsonElement state) =>
        new("typesafe/jev-1.14", state, questions);

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(
            Path.Combine(TestPaths.RepositoryRoot, "tests", "fixtures", "systemone", fileName)
        );
}
