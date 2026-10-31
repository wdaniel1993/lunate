using System.Text.Json;
using Lunate.Agent;
using Lunate.Contracts;
using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

public sealed class DecisionPrimitiveTests
{
    [Fact]
    public async Task One_option_choice_is_refused_before_a_model_call()
    {
        using JsonDocument state = JsonDocument.Parse("\"state\"");
        var request = new DecisionRequest(
            state.RootElement,
            [new DecisionQuestion("choice", "Choose", DecisionQuestionKind.Choice, ["only"])]
        );
        var client = new ScriptedChatClient();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            DecisionEngine.EvaluateAsync(
                request,
                client,
                "decision-model",
                "adapter",
                TestContext.Current.CancellationToken
            )
        );

        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Several_questions_return_keyed_answers_from_one_exchange()
    {
        var client = new ScriptedChatClient().Enqueue(
            LoopScripts.Text(
                """{"results":{"confirm":{"answer":"yes","probabilities":{"yes":0.7,"no":0.3}},"size":{"answer":"large","probabilities":{"small":0.2,"large":0.8}}}}"""
            ),
            LoopScripts.Stop()
        );
        var request = new DecisionRequest(
            State("\"release candidate\""),
            [
                new DecisionQuestion("confirm", "Is it ready?", DecisionQuestionKind.YesNo),
                new DecisionQuestion(
                    "size",
                    "Which size?",
                    DecisionQuestionKind.Choice,
                    ["small", "large"]
                ),
            ]
        );

        DecisionResponse response = await DecisionEngine.EvaluateAsync(
            request,
            client,
            "decision-model",
            "adapter",
            TestContext.Current.CancellationToken
        );

        Assert.Single(client.Requests);
        Assert.Equal(ChatResponseFormat.Json, client.Requests[0].Options?.ResponseFormat);
        Assert.Contains("\"kind\":\"yes/no\"", client.Requests[0].Messages[1].Text, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"choice\"", client.Requests[0].Messages[1].Text, StringComparison.Ordinal);
        Assert.Equal(["confirm", "size"], response.Results.Keys);
        Assert.Equal("yes", response.Results["confirm"].Answer);
        Assert.Equal(0.7d, response.Results["confirm"].Probability);
        Assert.Equal(["yes", "no"], response.Results["confirm"].Distribution.Select(item => item.Answer));
        Assert.Equal(0.3d, response.Results["confirm"].Distribution[1].Probability);
        Assert.Equal("large", response.Results["size"].Answer);
        Assert.Equal(0.8d, response.Results["size"].Probability);
        Assert.Equal("decision-model", response.Results["size"].ModelId);
        Assert.Equal("adapter", response.Results["size"].Method);
    }

    [Fact]
    public async Task Choice_with_256_answers_is_refused_before_a_model_call()
    {
        var request = new DecisionRequest(
            State("\"state\""),
            [
                new DecisionQuestion(
                    "choice",
                    "Choose",
                    DecisionQuestionKind.Choice,
                    Enumerable.Range(0, 256).Select(index => $"option-{index}").ToArray()
                ),
            ]
        );
        var client = new ScriptedChatClient();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            DecisionEngine.EvaluateAsync(
                request,
                client,
                "decision-model",
                "adapter",
                TestContext.Current.CancellationToken
            )
        );

        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Score_answers_keep_the_declared_rubric_order()
    {
        var client = new ScriptedChatClient().Enqueue(
            LoopScripts.Text(
                """{"results":{"quality":{"answer":"weak","probabilities":{"strong":0.2,"reject":0.3,"weak":0.5}}}}"""
            ),
            LoopScripts.Stop()
        );
        var request = new DecisionRequest(
            State("\"patch\""),
            [
                new DecisionQuestion(
                    "quality",
                    "Score this patch",
                    DecisionQuestionKind.Score,
                    ["reject", "weak", "strong"]
                ),
            ]
        );

        DecisionResponse response = await DecisionEngine.EvaluateAsync(
            request,
            client,
            "decision-model",
            "adapter",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            ["reject", "weak", "strong"],
            response.Results["quality"].Distribution.Select(item => item.Answer)
        );
        Assert.Contains("\"kind\":\"score\"", client.Requests.Single().Messages[1].Text, StringComparison.Ordinal);
        Assert.Equal(0.5d, response.Results["quality"].Probability);
    }

    [Fact]
    public async Task Native_client_receives_the_original_request()
    {
        var request = new DecisionRequest(
            State("\"state\""),
            [new DecisionQuestion("confirm", "Is it ready?", DecisionQuestionKind.YesNo)]
        );
        var native = new NativeDecisionClient(
            """{"results":{"confirm":{"answer":"yes","probabilities":{"yes":0.7,"no":0.3}}}}"""
        );
        var client = new ScriptedChatClient { NativeDecisionClient = native };

        DecisionResponse response = await DecisionEngine.EvaluateAsync(
            request,
            client,
            "decision-model",
            "native",
            TestContext.Current.CancellationToken
        );

        Assert.Same(request, native.Request);
        Assert.Empty(client.Requests);
        Assert.Equal("native", response.Results["confirm"].Method);
    }

    [Fact]
    public async Task Json_path_question_receives_only_the_targeted_field()
    {
        var client = new ScriptedChatClient().Enqueue(
            LoopScripts.Text(
                """{"results":{"header":{"answer":"yes","probabilities":{"yes":0.9,"no":0.1}}}}"""
            ),
            LoopScripts.Stop()
        );
        var request = new DecisionRequest(
            State("""{"diff":{"header":"safe"},"secret":"not for this question"}"""),
            [
                new DecisionQuestion(
                    "header",
                    "Is the header safe?",
                    DecisionQuestionKind.YesNo,
                    JsonPath: "/diff/header"
                ),
            ]
        );

        DecisionResponse response = await DecisionEngine.EvaluateAsync(
            request,
            client,
            "decision-model",
            "adapter",
            TestContext.Current.CancellationToken
        );

        string modelRequest = client.Requests.Single().Messages[1].Text;
        Assert.Contains("\"state\":\"safe\"", modelRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("not for this question", modelRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("/diff/header", modelRequest, StringComparison.Ordinal);
        Assert.Equal("yes", response.Results["header"].Answer);
    }

    [Fact]
    public async Task Invalid_response_is_retried_once_then_fails_naming_the_model()
    {
        var client = new ScriptedChatClient()
            .Enqueue(LoopScripts.Text("{\"results\":{}}"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("{\"results\":{}}"), LoopScripts.Stop());
        var request = new DecisionRequest(
            State("\"state\""),
            [new DecisionQuestion("confirm", "Is it ready?", DecisionQuestionKind.YesNo)]
        );

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

    [Theory]
    [InlineData("1.2", "0.3")]
    [InlineData("0.6", "0.3")]
    public async Task Invalid_yes_no_probabilities_are_rejected(string yes, string no)
    {
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Text($"{{\"results\":{{\"confirm\":{{\"answer\":\"yes\",\"probabilities\":{{\"yes\":{yes},\"no\":{no}}}}}}}}}"),
                LoopScripts.Stop()
            )
            .Enqueue(
                LoopScripts.Text("""{"results":{"confirm":{"answer":"yes","probabilities":{"yes":0.5,"no":0.5}}}}"""),
                LoopScripts.Stop()
            );
        var request = new DecisionRequest(
            State("\"state\""),
            [new DecisionQuestion("confirm", "Is it ready?", DecisionQuestionKind.YesNo)]
        );

        DecisionResponse response = await DecisionEngine.EvaluateAsync(
            request,
            client,
            "decision-model",
            "adapter",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, client.Requests.Count);
        Assert.Equal(0.5d, response.Results["confirm"].Probability);
    }

    private static JsonElement State(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class NativeDecisionClient(string response) : INativeDecisionClient
    {
        public DecisionRequest? Request { get; private set; }

        public Task<string> GetDecisionAsync(
            DecisionRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Request = request;
            return Task.FromResult(response);
        }
    }
}
