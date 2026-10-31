using System.Text.Json;
using Lunate.Contracts;

namespace Lunate.Agent.Tests;

public sealed class DecisionValidatorTests
{
    [Theory]
    [InlineData(DecisionQuestionKind.YesNo, "yes,no")]
    [InlineData(DecisionQuestionKind.Choice, "small,large")]
    [InlineData(DecisionQuestionKind.Score, "reject,strong")]
    public async Task Undefined_answer_is_retried_for_each_kind(
        DecisionQuestionKind kind,
        string answerList
    ) => await RejectTwiceAsync(kind, answerList, answer: "outside", ProbabilityCase.Valid);

    [Theory]
    [InlineData(DecisionQuestionKind.YesNo, "yes,no")]
    [InlineData(DecisionQuestionKind.Choice, "small,large")]
    [InlineData(DecisionQuestionKind.Score, "reject,strong")]
    public async Task Out_of_range_probability_is_retried_for_each_kind(
        DecisionQuestionKind kind,
        string answerList
    ) => await RejectTwiceAsync(kind, answerList, answer: "", ProbabilityCase.OutOfRange);

    [Theory]
    [InlineData(DecisionQuestionKind.YesNo, "yes,no")]
    [InlineData(DecisionQuestionKind.Choice, "small,medium,large")]
    [InlineData(DecisionQuestionKind.Score, "reject,weak,strong")]
    public async Task Distribution_that_misses_one_is_retried_for_each_kind(
        DecisionQuestionKind kind,
        string answerList
    ) => await RejectTwiceAsync(kind, answerList, answer: "", ProbabilityCase.MissesOne);

    [Fact]
    public async Task Non_finite_probability_is_retried()
    {
        const string response =
            """{"results":{"q":{"answer":"yes","probabilities":{"yes":1e400,"no":0}}}}""";
        var client = new ScriptedChatClient()
            .Enqueue(LoopScripts.Text(response), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text(response), LoopScripts.Stop());
        var request = new DecisionRequest(
            JsonSerializer.SerializeToElement("state"),
            [new DecisionQuestion("q", "Evaluate", DecisionQuestionKind.YesNo)]
        );

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            DecisionEngine.EvaluateAsync(
                request,
                client,
                "decision-model",
                "adapter",
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(2, client.Requests.Count);
    }

    private static async Task RejectTwiceAsync(
        DecisionQuestionKind kind,
        string answerList,
        string answer,
        ProbabilityCase probabilityCase
    )
    {
        string[] answers = answerList.Split(',');
        Dictionary<string, double> probabilities = answers.ToDictionary(
            value => value,
            _ =>
                probabilityCase switch
                {
                    ProbabilityCase.Valid => 1d / answers.Length,
                    ProbabilityCase.OutOfRange => 1.2d,
                    _ => 0.2d,
                },
            StringComparer.Ordinal
        );
        string selected = answer.Length == 0 ? answers[0] : answer;
        string raw = JsonSerializer.Serialize(
            new
            {
                results = new Dictionary<string, object>
                {
                    ["q"] = new { answer = selected, probabilities },
                },
            }
        );
        var client = new ScriptedChatClient()
            .Enqueue(LoopScripts.Text(raw), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text(raw), LoopScripts.Stop());
        var question = new DecisionQuestion(
            "q",
            "Evaluate",
            kind,
            kind == DecisionQuestionKind.YesNo ? null : answers
        );
        var request = new DecisionRequest(JsonSerializer.SerializeToElement("state"), [question]);

        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            DecisionEngine.EvaluateAsync(
                request,
                client,
                "decision-model",
                "adapter",
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(2, client.Requests.Count);
        Assert.Contains("decision-model", failure.Message, StringComparison.Ordinal);
    }

    private enum ProbabilityCase
    {
        Valid,
        OutOfRange,
        MissesOne,
    }
}
