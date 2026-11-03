using System.Text.Json;
using Lunate.Ai.Decisions;

namespace Lunate.Ai.SystemOne;

internal sealed record SystemOneResponse(
    string ResultsJson,
    DecisionUsage? Usage,
    IReadOnlyDictionary<string, double> ExpectedScores,
    string ModelId
);

internal static class SystemOneResponseMapper
{
    public static SystemOneResponse Map(string body, DecisionRequest request)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        if (
            root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("model", out JsonElement model)
            || model.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("answers", out JsonElement answers)
            || answers.ValueKind != JsonValueKind.Object
        )
        {
            throw new InvalidDataException("System One response must contain an 'answers' object.");
        }

        Dictionary<string, object> results = new(StringComparer.Ordinal);
        Dictionary<string, double> expectedScores = new(StringComparer.Ordinal);
        foreach (DecisionQuestion question in request.Questions)
        {
            if (!answers.TryGetProperty(question.Id, out JsonElement answer))
            {
                throw new InvalidDataException(
                    $"System One response is missing question '{question.Id}'."
                );
            }
            if (
                answer.ValueKind != JsonValueKind.Object
                || !answer.TryGetProperty("type", out JsonElement type)
                || type.GetString() != TypeName(question.Kind)
            )
            {
                throw new InvalidDataException(
                    $"System One response has an invalid answer type for question '{question.Id}'."
                );
            }

            if (question.Kind == DecisionQuestionKind.Score)
            {
                MappedScore score = MapScore(answer, question);
                results.Add(question.Id, score.Answer);
                expectedScores.Add(question.Id, score.ExpectedScore);
            }
            else
            {
                results.Add(question.Id, MapAnswer(answer, question));
            }
        }

        return new SystemOneResponse(
            JsonSerializer.Serialize(new { results }),
            MapUsage(root),
            expectedScores,
            model.GetString()!
        );
    }

    private static object MapAnswer(JsonElement answer, DecisionQuestion question) =>
        question.Kind switch
        {
            DecisionQuestionKind.Noul => MapNoul(answer),
            DecisionQuestionKind.Choice => MapChoice(answer),
            DecisionQuestionKind.Box => MapBox(answer),
            _ => throw new ArgumentOutOfRangeException(nameof(question), question.Kind, null),
        };

    private static object MapNoul(JsonElement answer)
    {
        double yes = RequiredFiniteNumber(answer, "noul");
        if (yes is < 0 or > 1)
        {
            throw new InvalidDataException("System One noul probability must be in [0, 1].");
        }
        return new
        {
            answer = yes >= 0.5 ? "yes" : "no",
            probabilities = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["yes"] = yes,
                ["no"] = 1 - yes,
            },
        };
    }

    private static object MapChoice(JsonElement answer)
    {
        if (
            !answer.TryGetProperty("choice", out JsonElement choice)
            || choice.ValueKind != JsonValueKind.String
            || !answer.TryGetProperty("probabilities", out JsonElement probabilities)
            || probabilities.ValueKind != JsonValueKind.Object
        )
        {
            throw new InvalidDataException("System One choice answer is incomplete.");
        }
        return new { answer = choice.GetString(), probabilities };
    }

    private static MappedScore MapScore(JsonElement answer, DecisionQuestion question)
    {
        if (
            !answer.TryGetProperty("legend", out JsonElement legend)
            || legend.ValueKind != JsonValueKind.Object
            || !answer.TryGetProperty("probabilities", out JsonElement probabilities)
            || probabilities.ValueKind != JsonValueKind.Object
        )
        {
            throw new InvalidDataException("System One score answer is incomplete.");
        }

        Dictionary<string, string> labelsByIndex = new(StringComparer.Ordinal);
        foreach (JsonProperty level in legend.EnumerateObject())
        {
            if (level.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("System One score legend must contain string levels.");
            }
            labelsByIndex.Add(level.Name, level.Value.GetString()!);
        }

        Dictionary<string, JsonElement> normalized = new(StringComparer.Ordinal);
        foreach (JsonProperty probability in probabilities.EnumerateObject())
        {
            if (!labelsByIndex.TryGetValue(probability.Name, out string? label))
            {
                throw new InvalidDataException("System One score probability has no legend level.");
            }
            normalized.Add(label, probability.Value.Clone());
        }

        IReadOnlyList<string> order = question.Answers!;
        string chosen = order
            .Where(normalized.ContainsKey)
            .MaxBy(label => normalized[label].GetDouble())
                       ?? throw new InvalidDataException("System One score answer has no levels.");
        double expectedScore = RequiredFiniteNumber(answer, "score");
        return new MappedScore(new { answer = chosen, probabilities = normalized }, expectedScore);
    }

    private static object MapBox(JsonElement answer)
    {
        if (
            !answer.TryGetProperty("box", out JsonElement box)
            || !answer.TryGetProperty("confidence", out JsonElement confidence)
        )
        {
            throw new InvalidDataException("System One box answer is incomplete.");
        }
        return new { box, confidence };
    }

    private static DecisionUsage? MapUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out JsonElement usage))
        {
            return null;
        }
        long? input = OptionalInt64(usage, "input_tokens");
        long? output = OptionalInt64(usage, "output_tokens");
        double? cost = usage.TryGetProperty("cost", out JsonElement costElement)
            && costElement.TryGetDouble(out double parsedCost)
            && double.IsFinite(parsedCost)
                ? parsedCost
                : null;
        return new DecisionUsage(input, output, cost, IsProviderReported: cost.HasValue);
    }

    private static long? OptionalInt64(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetInt64(out long parsed)
            ? parsed
            : null;

    private static double RequiredFiniteNumber(JsonElement element, string property)
    {
        if (
            !element.TryGetProperty(property, out JsonElement value)
            || !value.TryGetDouble(out double parsed)
            || !double.IsFinite(parsed)
        )
        {
            throw new InvalidDataException($"System One answer requires a finite '{property}' number.");
        }
        return parsed;
    }

    private static string TypeName(DecisionQuestionKind kind) => kind switch
    {
        DecisionQuestionKind.Noul => "noul",
        DecisionQuestionKind.Choice => "choice",
        DecisionQuestionKind.Score => "score",
        DecisionQuestionKind.Box => "box",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private sealed record MappedScore(object Answer, double ExpectedScore);
}
