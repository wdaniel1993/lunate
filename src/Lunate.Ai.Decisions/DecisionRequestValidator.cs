using System.Text.Json;

namespace Lunate.Ai.Decisions;

internal static class DecisionRequestValidator
{
    public static void Validate(DecisionRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModelId);
        if (request.State.ValueKind is JsonValueKind.Undefined)
        {
            throw new ArgumentException(
                "A decision request must contain a JSON state.",
                nameof(request)
            );
        }
        ArgumentNullException.ThrowIfNull(request.Questions);
        if (request.Questions.Count == 0)
        {
            throw new ArgumentException(
                "A decision request must contain at least one question.",
                nameof(request)
            );
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (DecisionQuestion question in request.Questions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(question.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(question.Instructions);
            if (!ids.Add(question.Id))
            {
                throw new ArgumentException(
                    $"Question id '{question.Id}' occurs more than once.",
                    nameof(request)
                );
            }

            if (question.Kind == DecisionQuestionKind.Box)
            {
                continue;
            }

            int maximum = question.Kind == DecisionQuestionKind.Choice ? 255 : int.MaxValue;
            if (question.Answers is not { } answers || answers.Count < 2 || answers.Count > maximum)
            {
                throw new ArgumentException(
                    $"Question '{question.Id}' has an invalid answer set.",
                    nameof(request)
                );
            }
            if (
                question.Kind == DecisionQuestionKind.Noul
                && !answers.SequenceEqual(["yes", "no"], StringComparer.Ordinal)
            )
            {
                throw new ArgumentException(
                    $"Noul question '{question.Id}' must define yes and no answers.",
                    nameof(request)
                );
            }

            HashSet<string> uniqueAnswers = new(StringComparer.Ordinal);
            foreach (string answer in answers)
            {
                if (string.IsNullOrWhiteSpace(answer) || !uniqueAnswers.Add(answer))
                {
                    throw new ArgumentException(
                        $"Question '{question.Id}' answers must be non-empty and unique.",
                        nameof(request)
                    );
                }
            }
        }
    }
}
