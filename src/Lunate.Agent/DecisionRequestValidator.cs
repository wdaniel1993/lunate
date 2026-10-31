using System.Globalization;
using System.Text.Json;
using Lunate.Contracts;

namespace Lunate.Agent;

internal static class DecisionRequestValidator
{
    public static IReadOnlyList<PreparedDecisionQuestion> Prepare(DecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Questions);
        if (request.Questions.Count == 0)
        {
            throw new ArgumentException(
                "A decision request must contain at least one question.",
                nameof(request)
            );
        }
        if (request.State.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException(
                "A decision request must contain a text or JSON state.",
                nameof(request)
            );
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        List<PreparedDecisionQuestion> prepared = [];
        foreach (DecisionQuestion question in request.Questions)
        {
            ArgumentNullException.ThrowIfNull(question);
            ArgumentException.ThrowIfNullOrWhiteSpace(question.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(question.Text);
            if (!ids.Add(question.Id))
            {
                throw new ArgumentException(
                    $"Question id '{question.Id}' occurs more than once in the decision request.",
                    nameof(request)
                );
            }

            IReadOnlyList<string> answers = question.Kind switch
            {
                DecisionQuestionKind.YesNo => ["yes", "no"],
                DecisionQuestionKind.Choice => ValidateAnswers(question, minimum: 2, maximum: 255),
                DecisionQuestionKind.Score => ValidateAnswers(
                    question,
                    minimum: 2,
                    maximum: int.MaxValue
                ),
                _ => throw new ArgumentException(
                    $"Question '{question.Id}' has an unsupported kind.",
                    nameof(request)
                ),
            };

            JsonElement state = question.JsonPath is { } path
                ? ResolveJsonPath(request.State, path, question.Id)
                : request.State;
            prepared.Add(new PreparedDecisionQuestion(question, answers, state));
        }

        return prepared;
    }

    public static DecisionAnswer ValidateResult(
        PreparedDecisionQuestion question,
        string? answer,
        IReadOnlyDictionary<string, double> probabilities,
        string modelId,
        string method
    )
    {
        if (!question.Answers.Contains(answer, StringComparer.Ordinal))
        {
            throw Invalid(question.Question.Id, "the chosen answer is not defined by the question");
        }

        if (probabilities.Count != question.Answers.Count)
        {
            throw Invalid(
                question.Question.Id,
                "the distribution must contain every defined answer exactly once"
            );
        }

        List<DecisionProbability> distribution = [];
        double sum = 0d;
        foreach (string definedAnswer in question.Answers)
        {
            if (!probabilities.TryGetValue(definedAnswer, out double probability))
            {
                throw Invalid(
                    question.Question.Id,
                    $"the distribution is missing '{definedAnswer}'"
                );
            }

            if (!double.IsFinite(probability) || probability is < 0d or > 1d)
            {
                throw Invalid(
                    question.Question.Id,
                    $"probability for '{definedAnswer}' must be finite and between 0 and 1"
                );
            }

            sum += probability;
            distribution.Add(new DecisionProbability(definedAnswer, probability));
        }

        if (Math.Abs(sum - 1d) > 0.000001d)
        {
            throw Invalid(question.Question.Id, "the probabilities must sum to 1 within 0.000001");
        }

        double chosenProbability = probabilities[answer!];
        return new DecisionAnswer(answer!, chosenProbability, distribution, modelId, method);
    }

    private static IReadOnlyList<string> ValidateAnswers(
        DecisionQuestion question,
        int minimum,
        int maximum
    )
    {
        if (
            question.Answers is not { } answers
            || answers.Count < minimum
            || answers.Count > maximum
        )
        {
            throw new ArgumentException(
                question.Kind == DecisionQuestionKind.Choice
                    ? $"Choice question '{question.Id}' must define between 2 and 255 answers."
                    : $"Score question '{question.Id}' must define at least 2 rubric levels.",
                nameof(question)
            );
        }

        HashSet<string> distinct = new(StringComparer.Ordinal);
        foreach (string answer in answers)
        {
            if (string.IsNullOrWhiteSpace(answer) || !distinct.Add(answer))
            {
                throw new ArgumentException(
                    $"Question '{question.Id}' answers must be non-empty and unique.",
                    nameof(question)
                );
            }
        }

        return answers.ToArray();
    }

    private static JsonElement ResolveJsonPath(JsonElement state, string path, string questionId)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/')
        {
            throw new ArgumentException(
                $"Question '{questionId}' JSON path must be an absolute path beginning with '/'.",
                nameof(path)
            );
        }

        JsonElement current = state;
        foreach (string token in path.Split('/').Skip(1))
        {
            string segment = token
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            if (
                current.ValueKind == JsonValueKind.Object
                && current.TryGetProperty(segment, out JsonElement property)
            )
            {
                current = property;
            }
            else if (
                current.ValueKind == JsonValueKind.Array
                && int.TryParse(
                    segment,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int index
                )
                && index >= 0
                && index < current.GetArrayLength()
            )
            {
                current = current[index];
            }
            else
            {
                throw new ArgumentException(
                    $"Question '{questionId}' JSON path '{path}' does not resolve in the request state.",
                    nameof(path)
                );
            }
        }

        return current;
    }

    private static InvalidDataException Invalid(string questionId, string reason) =>
        new($"Decision question '{questionId}' is off-contract: {reason}.");
}

internal sealed record PreparedDecisionQuestion(
    DecisionQuestion Question,
    IReadOnlyList<string> Answers,
    JsonElement State
);
