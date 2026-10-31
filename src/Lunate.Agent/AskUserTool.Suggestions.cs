using System.Globalization;
using System.Text.Json;
using Lunate.Contracts;

namespace Lunate.Agent;

internal sealed partial class AskUserTool
{
    private static async Task<List<UserQuestion>> AddDecisionSuggestionsAsync(
        List<UserQuestion> questions,
        Func<DecisionRequest, CancellationToken, Task<DecisionResponse>>? decideAsync,
        ToolContext context,
        CancellationToken cancellationToken
    )
    {
        string[] ids =
        [
            .. Enumerable
                .Range(1, questions.Count)
                .Select(index => $"question-{index.ToString(CultureInfo.InvariantCulture)}"),
        ];
        if (decideAsync is null)
        {
            EmitUnavailable(context, ids);
            return questions;
        }

        DecisionRequest request = new(
            JsonSerializer.SerializeToElement(
                questions.Select(question => new
                {
                    question.Header,
                    question.Question,
                    options = question.Options.Select(option => new
                    {
                        option.Label,
                        option.Description,
                    }),
                })
            ),
            questions
                .Select(
                    (question, index) =>
                        new DecisionQuestion(
                            ids[index],
                            question.Question,
                            DecisionQuestionKind.Choice,
                            question.Options.Select(option => option.Label).ToArray()
                        )
                )
                .ToArray()
        );

        DecisionResponse response;
        try
        {
            response = await decideAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return questions;
        }

        List<UserQuestion> suggested = new(questions.Count);
        for (int index = 0; index < questions.Count; index++)
        {
            UserQuestion question = questions[index];
            string id = ids[index];
            if (!response.Results.TryGetValue(id, out DecisionAnswer? answer))
            {
                suggested.Add(question);
                continue;
            }

            bool matched = question.Options.Any(option => option.Label == answer.Answer);
            if (!matched)
            {
                suggested.Add(question);
                continue;
            }

            suggested.Add(
                question with
                {
                    Options = question
                        .Options.Select(option =>
                            option.Label == answer.Answer
                                ? option with
                                {
                                    DecisionSuggestedProbability = answer.Probability,
                                }
                                : option
                        )
                        .ToArray(),
                }
            );
            EmitEvaluated(context, id, answer);
        }

        return suggested;
    }

    private static void EmitUnavailable(ToolContext context, IReadOnlyList<string> questionIds)
    {
        if (context.RunId is not { } runId)
        {
            return;
        }

        foreach (string questionId in questionIds)
        {
            context.Events.Emit(new DecisionUnavailable(runId) { QuestionId = questionId });
        }
    }

    private static void EmitEvaluated(ToolContext context, string questionId, DecisionAnswer answer)
    {
        if (context.RunId is not { } runId)
        {
            return;
        }

        context.Events.Emit(
            new DecisionEvaluated(
                runId,
                answer.ModelId,
                answer.Method,
                answer.Answer,
                answer.Probability
            )
            {
                QuestionId = questionId,
                Answer = answer.Answer,
            }
        );
    }
}
