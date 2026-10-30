using System.Globalization;
using System.Text.Json;
using Lunate.Agent;
using ModelContextProtocol.Protocol;

namespace Lunate.Protocols;

/// <summary>Maps MCP form requests to the agent's shared question interaction.</summary>
internal static class McpElicitationHandler
{
    internal static async ValueTask<ElicitResult> HandleAsync(
        ElicitRequestParams? request,
        string serverName,
        McpElicitationOutcome outcome,
        CancellationToken ct
    )
    {
        if (request is null)
        {
            return Result("decline");
        }

        var interaction = outcome.CurrentInteraction;
        if (interaction is null)
        {
            outcome.RecordUnavailable();
            return Result("cancel");
        }

        if (!TryMapRequest(request, serverName, out List<MappedQuestion>? mapped))
        {
            return Result("decline");
        }

        List<MappedQuestion> questions = mapped!;
        UserQuestionResult answer = await interaction(
                questions.Select(question => question.Question).ToArray(),
                ct
            )
            .ConfigureAwait(false);
        if (answer.Status == UserQuestionStatus.Unavailable)
        {
            outcome.RecordUnavailable();
            return Result("cancel");
        }

        if (answer.Status != UserQuestionStatus.Answered)
        {
            return Result("cancel");
        }

        return TryBuildContent(
            questions,
            answer.Answers,
            out Dictionary<string, JsonElement>? content
        )
            ? new ElicitResult { Action = "accept", Content = content }
            : Result("decline");
    }

    private static bool TryMapRequest(
        ElicitRequestParams request,
        string serverName,
        out List<MappedQuestion>? mapped
    )
    {
        mapped = null;
        ElicitRequestParams.RequestSchema? schema = request.RequestedSchema;
        if (
            !string.Equals(request.Mode, "form", StringComparison.Ordinal)
            || schema is not { Properties.Count: > 0 }
            || schema.Properties.Keys.Any(string.IsNullOrWhiteSpace)
        )
        {
            return false;
        }

        HashSet<string> required = new(schema.Required ?? [], StringComparer.Ordinal);
        if (required.Any(name => !schema.Properties.ContainsKey(name)))
        {
            return false;
        }

        List<MappedQuestion> questions = [];
        foreach (
            (
                string name,
                ElicitRequestParams.PrimitiveSchemaDefinition property
            ) in schema.Properties
        )
        {
            if (
                !TryMapField(
                    name,
                    property,
                    request.Message,
                    serverName,
                    required,
                    out MappedQuestion? question
                )
            )
            {
                return false;
            }

            questions.Add(question!);
        }

        mapped = questions;
        return true;
    }

    private static bool TryMapField(
        string name,
        ElicitRequestParams.PrimitiveSchemaDefinition schema,
        string message,
        string serverName,
        IReadOnlySet<string> required,
        out MappedQuestion? mapped
    )
    {
        mapped = null;
        bool isArray =
            schema
            is ElicitRequestParams.UntitledMultiSelectEnumSchema
                or ElicitRequestParams.TitledMultiSelectEnumSchema;
        if (!TryGetOptions(schema, out List<MappedOption> options))
        {
            return false;
        }

        string type = schema.Type ?? string.Empty;
        if (
            isArray
                ? !string.Equals(type, "array", StringComparison.Ordinal) || options.Count == 0
                : type is not ("string" or "number" or "integer" or "boolean")
                    || (options.Count > 0 && type != "string")
        )
        {
            return false;
        }

        string title = string.IsNullOrWhiteSpace(schema.Title) ? name : schema.Title;
        string question = string.IsNullOrWhiteSpace(schema.Description)
            ? message
            : message + Environment.NewLine + schema.Description;
        UserQuestion userQuestion = new(
            $"{serverName}: {title}",
            question,
            options.Select(option => option.Option).ToArray(),
            MultiSelect: isArray,
            AllowFreeText: options.Count == 0
        );
        mapped = new MappedQuestion(
            name,
            type,
            userQuestion,
            options,
            required.Contains(name),
            isArray
        );
        return true;
    }

    private static bool TryGetOptions(
        ElicitRequestParams.PrimitiveSchemaDefinition schema,
        out List<MappedOption> options
    )
    {
        options = schema switch
        {
            ElicitRequestParams.UntitledSingleSelectEnumSchema single =>
            [
                .. (single.Enum ?? []).Select(value => new MappedOption(
                    value,
                    value,
                    single.Description
                )),
            ],
            ElicitRequestParams.TitledSingleSelectEnumSchema single =>
            [
                .. (single.OneOf ?? []).Select(value => new MappedOption(
                    value.Const,
                    value.Title,
                    single.Description
                )),
            ],
            ElicitRequestParams.UntitledMultiSelectEnumSchema multi =>
            [
                .. (multi.Items?.Enum ?? []).Select(value => new MappedOption(
                    value,
                    value,
                    multi.Description
                )),
            ],
            ElicitRequestParams.TitledMultiSelectEnumSchema multi =>
            [
                .. (multi.Items?.AnyOf ?? []).Select(value => new MappedOption(
                    value.Const,
                    value.Title,
                    multi.Description
                )),
            ],
            _ => [],
        };

        return options.All(option =>
                !string.IsNullOrWhiteSpace(option.Value)
                && !string.IsNullOrWhiteSpace(option.Option.Label)
            )
            && options
                .Select(option => option.Option.Label)
                .Distinct(StringComparer.Ordinal)
                .Count() == options.Count;
    }

    private static bool TryBuildContent(
        IReadOnlyList<MappedQuestion> questions,
        IReadOnlyList<UserQuestionAnswer> answers,
        out Dictionary<string, JsonElement>? content
    )
    {
        content = null;
        if (answers.Count != questions.Count)
        {
            return false;
        }

        Dictionary<string, JsonElement> values = new(StringComparer.Ordinal);
        for (int index = 0; index < questions.Count; index++)
        {
            MappedQuestion question = questions[index];
            UserQuestionAnswer answer = answers[index];
            if (question.IsArray)
            {
                if (
                    answer.Text is not null
                    || !TryGetSelectedValues(
                        question,
                        answer.SelectedOptions,
                        out string[]? selected
                    )
                )
                {
                    return false;
                }

                values[question.Name] = JsonSerializer.SerializeToElement(selected);
            }
            else if (question.Options.Count > 0)
            {
                if (
                    answer.Text is not null
                    || !TryGetSelectedValues(
                        question,
                        answer.SelectedOptions,
                        out string[]? selected
                    )
                    || selected is not { Length: 1 } selectedValues
                )
                {
                    return false;
                }

                values[question.Name] = JsonSerializer.SerializeToElement(selectedValues[0]);
            }
            else
            {
                if (answer.SelectedOptions.Count > 0)
                {
                    return false;
                }

                if (answer.Text is null)
                {
                    if (question.Required)
                    {
                        return false;
                    }

                    continue;
                }

                if (!TryParsePrimitive(question.Type, answer.Text, out JsonElement value))
                {
                    return false;
                }

                values[question.Name] = value;
            }
        }

        if (questions.Any(question => question.Required && !values.ContainsKey(question.Name)))
        {
            return false;
        }

        content = values;
        return true;
    }

    private static bool TryGetSelectedValues(
        MappedQuestion question,
        IReadOnlyList<string> selected,
        out string[]? values
    )
    {
        values = null;
        List<string> mapped = [];
        foreach (string label in selected)
        {
            MappedOption? option = question.Options.FirstOrDefault(option =>
                string.Equals(option.Option.Label, label, StringComparison.Ordinal)
            );
            if (option is null || mapped.Contains(option.Value, StringComparer.Ordinal))
            {
                return false;
            }

            mapped.Add(option.Value);
        }

        if (!question.IsArray && mapped.Count > 1)
        {
            return false;
        }

        values = [.. mapped];
        return true;
    }

    private static bool TryParsePrimitive(string type, string text, out JsonElement value)
    {
        value = default;
        switch (type)
        {
            case "string":
                value = JsonSerializer.SerializeToElement(text);
                return true;
            case "number":
            case "integer":
                if (
                    !double.TryParse(
                        text,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double number
                    )
                    || !double.IsFinite(number)
                    || (type == "integer" && number != Math.Truncate(number))
                )
                {
                    return false;
                }

                value = JsonSerializer.SerializeToElement(number);
                return true;
            case "boolean" when bool.TryParse(text, out bool boolean):
                value = JsonSerializer.SerializeToElement(boolean);
                return true;
            default:
                return false;
        }
    }

    private static ElicitResult Result(string action) => new() { Action = action };

    private sealed record MappedQuestion(
        string Name,
        string Type,
        UserQuestion Question,
        IReadOnlyList<MappedOption> Options,
        bool Required,
        bool IsArray
    );

    private sealed record MappedOption(string Value, string Label, string? Description)
    {
        public UserQuestionOption Option { get; } =
            new(Label, Description ?? string.Empty, Recommended: false);
    }
}
