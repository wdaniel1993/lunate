using System.Text.Json;
using Lunate.Ai.Decisions;

namespace Lunate.Ai.SystemOne;

internal static class SystemOneRequestMapper
{
    private const int MaximumBodyBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 70,
    };

    public static byte[] Serialize(DecisionRequest request, string modelId)
    {
        ValidateState(request.State, 0);
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(Create(request, modelId), SerializerOptions);
        if (body.Length > MaximumBodyBytes)
        {
            throw new InvalidDataException("Decision request exceeds the 32 MiB body limit.");
        }
        return body;
    }

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

    private static void ValidateState(JsonElement state, int parentDepth)
    {
        int depth = parentDepth + (state.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? 1 : 0);
        if (depth > 64)
        {
            throw new InvalidDataException("Decision state exceeds the 64-level nesting limit.");
        }
        if (
            state.ValueKind == JsonValueKind.Number
            && (!state.TryGetDouble(out double number) || !double.IsFinite(number))
        )
        {
            throw new InvalidDataException("Decision state numbers must be finite.");
        }

        if (state.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in state.EnumerateObject())
            {
                ValidateState(property.Value, depth);
            }
        }
        else if (state.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in state.EnumerateArray())
            {
                ValidateState(item, depth);
            }
        }
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
