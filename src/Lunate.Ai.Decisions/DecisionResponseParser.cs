using System.Text.Json;

namespace Lunate.Ai.Decisions;

internal static class DecisionResponseParser
{
    public static DecisionResult Parse(
        string text,
        DecisionRequest request,
        string modelId,
        string method
    )
    {
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;
        if (
            root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("results", out JsonElement results)
            || results.ValueKind != JsonValueKind.Object
        )
        {
            throw new InvalidDataException("response must contain a keyed 'results' object");
        }

        Dictionary<string, DecisionAnswer> answers = new(StringComparer.Ordinal);
        foreach (JsonProperty result in results.EnumerateObject())
        {
            DecisionQuestion? question = request.Questions.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, result.Name, StringComparison.Ordinal)
            );
            if (question is null || result.Value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"response contains an undefined question id '{result.Name}'"
                );
            }

            if (question.Kind == DecisionQuestionKind.Box)
            {
                if (
                    result.Value.TryGetProperty("answer", out _)
                    || result.Value.TryGetProperty("probabilities", out _)
                )
                {
                    throw new InvalidDataException(
                        $"box response for question '{result.Name}' must not contain an answer or distribution"
                    );
                }
                answers.Add(result.Name, ParseBox(result.Value, modelId, method));
                continue;
            }

            if (result.Value.TryGetProperty("box", out _))
            {
                throw new InvalidDataException(
                    $"response for non-box question '{result.Name}' contains a box answer"
                );
            }

            if (
                !result.Value.TryGetProperty("answer", out JsonElement answerElement)
                || answerElement.ValueKind != JsonValueKind.String
                || !result.Value.TryGetProperty(
                    "probabilities",
                    out JsonElement probabilitiesElement
                )
                || probabilitiesElement.ValueKind != JsonValueKind.Object
            )
            {
                throw new InvalidDataException(
                    $"response for question '{result.Name}' must contain an answer and probabilities object"
                );
            }

            Dictionary<string, double> probabilities = new(StringComparer.Ordinal);
            foreach (JsonProperty probability in probabilitiesElement.EnumerateObject())
            {
                if (
                    !probability.Value.TryGetDouble(out double value)
                    || !probabilities.TryAdd(probability.Name, value)
                )
                {
                    throw new InvalidDataException(
                        $"response for question '{result.Name}' has an invalid probability"
                    );
                }
            }

            answers.Add(
                result.Name,
                DecisionResultValidator.Validate(
                    question,
                    answerElement.GetString(),
                    probabilities,
                    modelId,
                    method
                )
            );
        }

        if (answers.Count != request.Questions.Count)
        {
            string missing = string.Join(
                ", ",
                request
                    .Questions.Where(question => !answers.ContainsKey(question.Id))
                    .Select(question => question.Id)
            );
            throw new InvalidDataException($"response is missing question results: {missing}");
        }

        return new DecisionResult(answers);
    }

    private static DecisionAnswer ParseBox(JsonElement result, string modelId, string method)
    {
        if (
            !result.TryGetProperty("box", out JsonElement box)
            || box.ValueKind != JsonValueKind.Object
            || !TryGetFinite(box, "top", out double top)
            || !TryGetFinite(box, "bottom", out double bottom)
            || !TryGetFinite(box, "left", out double left)
            || !TryGetFinite(box, "right", out double right)
            || !TryGetFinite(result, "confidence", out double confidence)
            || top is < 0 or > 1
            || bottom is < 0 or > 1
            || left is < 0 or > 1
            || right is < 0 or > 1
            || confidence is < 0 or > 1
            || top >= bottom
            || left >= right
        )
        {
            throw new InvalidDataException(
                "box answer must contain ordered fractions and confidence in [0, 1]"
            );
        }

        return new DecisionAnswer(
            "",
            confidence,
            [],
            modelId,
            method,
            confidence,
            new DecisionBox(top, bottom, left, right)
        );
    }

    private static bool TryGetFinite(JsonElement element, string name, out double value)
    {
        value = default;
        return element.TryGetProperty(name, out JsonElement number)
            && number.TryGetDouble(out value)
            && double.IsFinite(value);
    }
}
