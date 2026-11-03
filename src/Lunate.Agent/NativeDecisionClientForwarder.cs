using System.Text.Json;
using Lunate.Ai.Decisions;
using Lunate.Contracts;
using InternalDecisionAnswer = Lunate.Ai.Decisions.DecisionAnswer;
using InternalDecisionRequest = Lunate.Ai.Decisions.DecisionRequest;
using InternalDecisionQuestion = Lunate.Ai.Decisions.DecisionQuestion;
using InternalDecisionQuestionKind = Lunate.Ai.Decisions.DecisionQuestionKind;

namespace Lunate.Agent;

/// <summary>Exposes an internal decision client through the stable native-decision contract.</summary>
internal sealed class NativeDecisionClientForwarder(IDecisionClient inner, string modelId)
    : INativeDecisionClient
{
    public async Task<string> GetDecisionAsync(
        Lunate.Contracts.DecisionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        InternalDecisionQuestion[] questions = request
            .Questions.Select(question => new InternalDecisionQuestion(
                question.Id,
                question.Text,
                question.Kind switch
                {
                    Lunate.Contracts.DecisionQuestionKind.YesNo => InternalDecisionQuestionKind.Noul,
                    Lunate.Contracts.DecisionQuestionKind.Choice => InternalDecisionQuestionKind.Choice,
                    Lunate.Contracts.DecisionQuestionKind.Score => InternalDecisionQuestionKind.Score,
                    Lunate.Contracts.DecisionQuestionKind.Box => InternalDecisionQuestionKind.Box,
                    _ => throw new InvalidOperationException("The decision question kind is unsupported."),
                },
                question.Kind == Lunate.Contracts.DecisionQuestionKind.YesNo
                    ? ["yes", "no"]
                    : question.Answers,
                JsonPath: question.JsonPath
            ))
            .ToArray();
        DecisionResult response = await inner
            .GetDecisionAsync(
                new InternalDecisionRequest(modelId, request.State, questions),
                new DecisionOptions(ModelId: modelId),
                cancellationToken
            )
            .ConfigureAwait(false);

        Dictionary<string, object> results = new(StringComparer.Ordinal);
        foreach ((string id, InternalDecisionAnswer answer) in response.Results)
        {
            Dictionary<string, object> mapped = new(StringComparer.Ordinal)
            {
                ["answer"] = answer.Answer,
                ["probabilities"] = answer.Distribution.ToDictionary(
                    item => item.Answer,
                    item => item.Probability,
                    StringComparer.Ordinal
                ),
            };
            if (answer.Box is { } box)
            {
                mapped["box"] = new
                {
                    top = box.Top,
                    bottom = box.Bottom,
                    left = box.Left,
                    right = box.Right,
                };
                mapped["confidence"] = answer.Confidence;
            }
            results.Add(id, mapped);
        }
        return JsonSerializer.Serialize(new { results });
    }
}
