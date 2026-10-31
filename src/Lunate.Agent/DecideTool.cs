using System.Text.Json;
using Lunate.Contracts;

namespace Lunate.Agent;

internal sealed class DecideTool : ITool
{
    private static readonly JsonSerializerOptions OutputJsonOptions = new(
        JsonSerializerDefaults.Web
    );

    private static readonly JsonElement Schema = CreateSchema();

    public string Name => "decide";

    public string Description =>
        "Request information from the configured decision model about a state. Batch one or more named questions in one call. Results are advice only: they never approve tools, skip approval prompts, or answer questions asked of the user.";

    public JsonElement ParametersSchema => Schema;

    public ToolRisk Risk => ToolRisk.ReadOnly;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement args,
        ToolContext ctx,
        CancellationToken ct
    )
    {
        if (!TryParseRequest(args, out DecisionRequest? request, out string? error))
        {
            return new ToolResult(error!, IsError: true);
        }

        if (ctx.AgentState is not AgentToolContextState { DecideAsync: { } decide })
        {
            return new ToolResult(
                "The decision profile is unavailable. Continue without this advice or configure a decision profile.",
                IsError: true
            );
        }

        try
        {
            DecisionResponse response = await decide(request!, ct).ConfigureAwait(false);
            return new ToolResult(
                JsonSerializer.Serialize(new { results = response.Results }, OutputJsonOptions),
                IsError: false
            );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ToolResult(
                $"Decision request failed: {exception.Message} Continue without this advice or revise the questions and try again.",
                IsError: true
            );
        }
    }

    private static bool TryParseRequest(
        JsonElement args,
        out DecisionRequest? request,
        out string? error
    )
    {
        request = null;
        error = null;
        if (
            args.ValueKind != JsonValueKind.Object
            || !args.TryGetProperty("state", out JsonElement state)
            || !args.TryGetProperty("questions", out JsonElement questionsElement)
            || questionsElement.ValueKind != JsonValueKind.Array
            || questionsElement.GetArrayLength() == 0
        )
        {
            error =
                "decide requires a JSON state and one or more named questions; provide both fields and try again.";
            return false;
        }

        List<DecisionQuestion> questions = [];
        foreach (JsonElement questionElement in questionsElement.EnumerateArray())
        {
            if (
                questionElement.ValueKind != JsonValueKind.Object
                || !TryGetString(questionElement, "id", out string? id)
                || !TryGetString(questionElement, "text", out string? text)
                || !TryGetString(questionElement, "kind", out string? kind)
            )
            {
                error = "Each decide question needs a non-empty id, text, and kind.";
                return false;
            }

            DecisionQuestionKind questionKind = kind switch
            {
                "yes/no" => DecisionQuestionKind.YesNo,
                "choice" => DecisionQuestionKind.Choice,
                "score" => DecisionQuestionKind.Score,
                _ => (DecisionQuestionKind)(-1),
            };
            if (!Enum.IsDefined(questionKind))
            {
                error = "decide question kind must be 'yes/no', 'choice', or 'score'.";
                return false;
            }

            IReadOnlyList<string>? answers = null;
            if (questionElement.TryGetProperty("answers", out JsonElement answersElement))
            {
                if (
                    answersElement.ValueKind != JsonValueKind.Array
                    || answersElement
                        .EnumerateArray()
                        .Any(answer => answer.ValueKind != JsonValueKind.String)
                )
                {
                    error = "decide question answers must be an array of strings.";
                    return false;
                }

                answers = answersElement
                    .EnumerateArray()
                    .Select(answer => answer.GetString()!)
                    .ToArray();
            }

            string? jsonPath = null;
            if (questionElement.TryGetProperty("jsonPath", out JsonElement pathElement))
            {
                if (pathElement.ValueKind != JsonValueKind.String)
                {
                    error = "decide question jsonPath must be a string.";
                    return false;
                }

                jsonPath = pathElement.GetString();
            }

            questions.Add(new DecisionQuestion(id!, text!, questionKind, answers, jsonPath));
        }

        request = new DecisionRequest(state.Clone(), questions);
        return true;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (
            !element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString())
        )
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static JsonElement CreateSchema()
    {
        const string schemaJson = """
            {
              "type": "object",
              "properties": {
                "state": { "description": "The JSON state all questions evaluate." },
                "questions": {
                  "type": "array",
                  "minItems": 1,
                  "items": {
                    "type": "object",
                    "properties": {
                      "id": { "type": "string", "description": "Unique result key." },
                      "text": { "type": "string", "description": "Question evaluated against the state." },
                      "kind": { "type": "string", "enum": ["yes/no", "choice", "score"] },
                      "answers": { "type": "array", "items": { "type": "string" }, "description": "Ordered choice options or score levels; omit for yes/no." },
                      "jsonPath": { "type": "string", "description": "Optional JSON pointer into state." }
                    },
                    "required": ["id", "text", "kind"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["state", "questions"],
              "additionalProperties": false
            }
            """;

        using JsonDocument document = JsonDocument.Parse(schemaJson);
        return document.RootElement.Clone();
    }
}
