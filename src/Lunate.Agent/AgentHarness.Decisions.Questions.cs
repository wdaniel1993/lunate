using System.Text.Json;
using Lunate.Ai;
using Lunate.Contracts;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

public sealed partial class AgentHarness
{
    private async Task<IReadOnlyList<UserQuestion>> EvaluateQuestionSuggestionsAsync(
        string runId,
        IReadOnlyList<UserQuestion> questions,
        AgentEventChannel channel,
        CancellationToken ct
    )
    {
        UsageModelClient? model = ResolveUsageModel("decision");
        if (model is null)
        {
            for (int index = 0; index < questions.Count; index++)
            {
                channel.Emit(new DecisionUnavailable(runId) { QuestionId = QuestionId(index) });
            }

            return questions;
        }

        string modelId = model.Model.Id;
        try
        {
            bool native =
                model.Model.ApiType == "native"
                && model.Client.GetService(typeof(INativeDecisionClient)) is INativeDecisionClient;
            if (!native)
            {
                RequireCapability(
                    model.Model,
                    model.Model.Capabilities?.Features,
                    "structured-output",
                    "feature"
                );
            }

            string method = native ? "native" : "adapter";
            string[] ids = [.. Enumerable.Range(0, questions.Count).Select(QuestionId)];
            var request = new DecisionRequest(
                JsonSerializer.SerializeToElement(
                    questions.Select(
                        (question, index) =>
                            new
                            {
                                id = ids[index],
                                header = question.Header,
                                question = question.Question,
                                options = question.Options.Select(option => new
                                {
                                    option.Label,
                                    option.Description,
                                }),
                            }
                    )
                ),
                questions
                    .Select(
                        (question, index) =>
                            new DecisionQuestion(
                                ids[index],
                                $"Which option best answers: {question.Question}",
                                DecisionQuestionKind.Choice,
                                question.Options.Select(option => option.Label).ToArray()
                            )
                    )
                    .ToArray()
            );
            DecisionResponse response = await DecisionEngine.EvaluateAsync(
                request,
                model.Client,
                modelId,
                method,
                ct,
                (usage, resolvedModelId, decisionMethod, probability) =>
                    AccumulateUsage(
                        runId,
                        channel,
                        usage,
                        resolvedModelId,
                        UsageAttribution("decision", model, decisionMethod, probability)
                    )
            );

            UserQuestion[] suggested = new UserQuestion[questions.Count];
            for (int index = 0; index < questions.Count; index++)
            {
                DecisionAnswer answer = response.Results[ids[index]];
                channel.Emit(
                    new DecisionEvaluated(
                        runId,
                        answer.ModelId,
                        answer.Method,
                        answer.Answer,
                        answer.Probability
                    )
                    {
                        QuestionId = ids[index],
                        Answer = answer.Answer,
                    }
                );
                suggested[index] = questions[index] with
                {
                    Options = questions[index]
                        .Options.Select(option =>
                            option with
                            {
                                Recommended = option.Label == answer.Answer,
                            }
                        )
                        .ToArray(),
                };
            }

            return suggested;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            EmitSuggestionFailures(runId, modelId, exception.Message, questions, channel);
            return questions;
        }
        catch (Exception)
        {
            EmitSuggestionFailures(runId, modelId, "provider request failed", questions, channel);
            return questions;
        }
    }

    private static void EmitSuggestionFailures(
        string runId,
        string modelId,
        string reason,
        IReadOnlyList<UserQuestion> questions,
        AgentEventChannel channel
    )
    {
        for (int index = 0; index < questions.Count; index++)
        {
            channel.Emit(
                new DecisionFailed(runId, modelId, reason) { QuestionId = QuestionId(index) }
            );
        }
    }

    private static string QuestionId(int index) =>
        FormattableString.Invariant($"question-{index + 1}");
}
