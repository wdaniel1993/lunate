using System.Text;
using System.Text.Json;

namespace Lunate.Agent;

internal sealed class AskUserTool : ITool
{
    private const string DescriptionText =
        "Ask the user only to make decisions that belong to them. Do not ask permission to proceed, and do not ask for facts you can discover with your tools. Answers are information only and never approve tools. Ask 1–4 concise questions; each has 2–4 labelled options with one-line descriptions, single- or multi-select, and an optional recommended option. The user may also enter free text alongside or instead of selected options.";

    private static readonly JsonElement Schema = CreateSchema();

    public string Name => "ask_user";

    public string Description => DescriptionText;

    public JsonElement ParametersSchema => Schema;

    public ToolRisk Risk => ToolRisk.ReadOnly;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement args,
        ToolContext ctx,
        CancellationToken ct
    )
    {
        if (!TryParseQuestions(args, out List<UserQuestion>? questions, out string? error))
        {
            return new ToolResult(error!, IsError: true);
        }

        if (ctx.AgentState is not AgentToolContextState { AskUserAsync: { } askUser })
        {
            return Unanswered("no user present");
        }

        UserQuestionResult response = await askUser(questions!, ct).ConfigureAwait(false);
        return response.Status switch
        {
            UserQuestionStatus.Answered => FormatAnswers(questions!, response.Answers),
            UserQuestionStatus.Skipped => Unanswered("user skipped"),
            _ => Unanswered("no user present"),
        };
    }

    private static bool TryParseQuestions(
        JsonElement args,
        out List<UserQuestion>? questions,
        out string? error
    )
    {
        questions = null;
        error = null;
        if (
            args.ValueKind != JsonValueKind.Object
            || !args.TryGetProperty("questions", out JsonElement questionsElement)
            || questionsElement.ValueKind != JsonValueKind.Array
            || questionsElement.GetArrayLength() is < 1 or > 4
        )
        {
            error = InvalidRequest();
            return false;
        }

        List<UserQuestion> parsed = [];
        foreach (JsonElement questionElement in questionsElement.EnumerateArray())
        {
            if (
                questionElement.ValueKind != JsonValueKind.Object
                || !TryGetNonEmptyString(questionElement, "header", out string? header)
                || !TryGetNonEmptyString(questionElement, "question", out string? prompt)
                || !questionElement.TryGetProperty("options", out JsonElement optionsElement)
                || optionsElement.ValueKind != JsonValueKind.Array
                || optionsElement.GetArrayLength() is < 2 or > 4
                || !questionElement.TryGetProperty(
                    "multiSelect",
                    out JsonElement multiSelectElement
                )
                || multiSelectElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            )
            {
                error = InvalidRequest();
                return false;
            }

            List<UserQuestionOption> options = [];
            foreach (JsonElement optionElement in optionsElement.EnumerateArray())
            {
                if (
                    optionElement.ValueKind != JsonValueKind.Object
                    || !TryGetNonEmptyString(optionElement, "label", out string? label)
                    || !TryGetNonEmptyString(optionElement, "description", out string? description)
                    || description.Contains('\r')
                    || description.Contains('\n')
                )
                {
                    error = InvalidRequest();
                    return false;
                }

                options.Add(new UserQuestionOption(label, description, Recommended: false));
            }

            if (
                options.Select(option => option.Label).Distinct(StringComparer.Ordinal).Count()
                != options.Count
            )
            {
                error =
                    "ask_user option labels must be unique; use a distinct label for each option.";
                return false;
            }

            string? recommendedLabel = null;
            if (
                questionElement.TryGetProperty(
                    "recommendedOption",
                    out JsonElement recommendedElement
                )
            )
            {
                if (
                    recommendedElement.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(recommendedElement.GetString())
                )
                {
                    error = InvalidRequest();
                    return false;
                }

                recommendedLabel = recommendedElement.GetString();
                if (!options.Any(option => option.Label == recommendedLabel))
                {
                    error = "ask_user recommendedOption must match one of the option labels.";
                    return false;
                }
            }

            if (recommendedLabel is not null)
            {
                options =
                [
                    .. options
                        .OrderByDescending(option => option.Label == recommendedLabel)
                        .Select(option =>
                            option with
                            {
                                Recommended = option.Label == recommendedLabel,
                            }
                        ),
                ];
            }

            parsed.Add(
                new UserQuestion(
                    header,
                    prompt,
                    options,
                    multiSelectElement.GetBoolean(),
                    AllowFreeText: true
                )
            );
        }

        questions = parsed;
        return true;
    }

    private static bool TryGetNonEmptyString(
        JsonElement element,
        string propertyName,
        out string value
    )
    {
        value = string.Empty;
        if (
            !element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString())
        )
        {
            return false;
        }

        value = property.GetString()!;
        return true;
    }

    private static ToolResult FormatAnswers(
        IReadOnlyList<UserQuestion> questions,
        IReadOnlyList<UserQuestionAnswer> answers
    )
    {
        if (answers.Count != questions.Count)
        {
            return new ToolResult(
                "ask_user received an incomplete answer. Ask the user again with the original questions.",
                IsError: true
            );
        }

        StringBuilder output = new("User answers:");
        for (int index = 0; index < questions.Count; index++)
        {
            UserQuestionAnswer answer = answers[index];
            List<string> parts = [.. answer.SelectedOptions];
            if (!string.IsNullOrWhiteSpace(answer.Text))
            {
                parts.Add($"text: {answer.Text}");
            }

            output.AppendLine();
            output.Append(questions[index].Header);
            output.Append(": ");
            output.Append(parts.Count == 0 ? "no selection" : string.Join(", ", parts));
        }

        return new ToolResult(output.ToString(), IsError: false);
    }

    private static ToolResult Unanswered(string reason) =>
        new($"unanswered: {reason}. State an assumption and continue.", IsError: false);

    private static string InvalidRequest() =>
        "ask_user requires 1–4 questions and 2–4 options per question; provide non-empty headers, prompts, option labels and one-line descriptions, then ask again. No prompt was shown.";

    private static JsonElement CreateSchema()
    {
        const string schemaJson = """
            {
              "type": "object",
              "properties": {
                "questions": {
                  "type": "array",
                  "minItems": 1,
                  "maxItems": 4,
                  "items": {
                    "type": "object",
                    "properties": {
                      "header": { "type": "string", "description": "Short heading shown to the user." },
                      "question": { "type": "string", "description": "The decision the user needs to make." },
                      "options": {
                        "type": "array",
                        "minItems": 2,
                        "maxItems": 4,
                        "items": {
                          "type": "object",
                          "properties": {
                            "label": { "type": "string" },
                            "description": { "type": "string", "description": "One-line explanation of this option." }
                          },
                          "required": ["label", "description"],
                          "additionalProperties": false
                        }
                      },
                      "multiSelect": { "type": "boolean", "description": "Whether the user may choose multiple options." },
                      "recommendedOption": { "type": "string", "description": "Optional option label to show first as a recommendation; it is not selected automatically." }
                    },
                    "required": ["header", "question", "options", "multiSelect"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["questions"],
              "additionalProperties": false
            }
            """;

        using JsonDocument document = JsonDocument.Parse(schemaJson);
        return document.RootElement.Clone();
    }
}

internal sealed record UserQuestion(
    string Header,
    string Question,
    IReadOnlyList<UserQuestionOption> Options,
    bool MultiSelect,
    bool AllowFreeText
);

internal sealed record UserQuestionOption(string Label, string Description, bool Recommended);

internal sealed record UserQuestionAnswer(IReadOnlyList<string> SelectedOptions, string? Text);

internal sealed record UserQuestionResult(
    UserQuestionStatus Status,
    IReadOnlyList<UserQuestionAnswer> Answers
);

internal enum UserQuestionStatus
{
    Answered,
    Skipped,
    Unavailable,
}
