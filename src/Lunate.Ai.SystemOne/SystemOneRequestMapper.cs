using Lunate.Ai.Decisions;
using System.Text.Json;

namespace Lunate.Ai.SystemOne;

internal static class SystemOneRequestMapper
{
    public static bool ContainsImagePart(JsonElement state)
    {
        if (
            state.ValueKind == JsonValueKind.Object
            && state.TryGetProperty("type", out JsonElement type)
            && type.ValueKind == JsonValueKind.String
            && type.GetString() == "image"
            && state.TryGetProperty("mimeType", out JsonElement mimeType)
            && mimeType.ValueKind == JsonValueKind.String
            && mimeType.GetString()!.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            && (
                state.TryGetProperty("data", out JsonElement data)
                    && data.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(data.GetString())
                || state.TryGetProperty("url", out JsonElement url)
                    && url.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(url.GetString())
            )
        )
        {
            return true;
        }

        return state.ValueKind switch
        {
            JsonValueKind.Object => state.EnumerateObject().Any(property => ContainsImagePart(property.Value)),
            JsonValueKind.Array => state.EnumerateArray().Any(ContainsImagePart),
            _ => false,
        };
    }

    public static object Create(DecisionRequest request, string modelId)
    {
        Dictionary<string, object> questions = new(StringComparer.Ordinal);
        foreach (DecisionQuestion question in request.Questions)
        {
            Dictionary<string, object> mapped = new(StringComparer.Ordinal)
            {
                ["type"] = TypeName(question.Kind),
                ["instructions"] = question.Instructions,
            };
            if (Criteria(question) is { } criteria)
            {
                mapped["criteria"] = criteria;
            }
            questions.Add(question.Id, mapped);
        }

        return new
        {
            model = modelId,
            state = request.State,
            questions,
        };
    }

    private static object? Criteria(DecisionQuestion question) => question.Kind switch
    {
        DecisionQuestionKind.Noul => question.Criteria,
        DecisionQuestionKind.Choice => question.Criteria ?? question.Answers!.ToDictionary(
            answer => answer,
            answer => answer,
            StringComparer.Ordinal
        ),
        DecisionQuestionKind.Score => question.Answers!.Select(answer =>
            question.Criteria is not null && question.Criteria.TryGetValue(answer, out string? text)
                ? text
                : answer
        ).ToArray(),
        DecisionQuestionKind.Box => null,
        _ => throw new ArgumentOutOfRangeException(nameof(question), question.Kind, null),
    };

    private static string TypeName(DecisionQuestionKind kind) => kind switch
    {
        DecisionQuestionKind.Noul => "noul",
        DecisionQuestionKind.Choice => "choice",
        DecisionQuestionKind.Score => "score",
        DecisionQuestionKind.Box => "box",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
