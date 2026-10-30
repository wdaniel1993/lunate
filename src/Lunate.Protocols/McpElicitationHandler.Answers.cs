using System.Globalization;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Protocols;

internal static partial class McpElicitationHandler
{
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
}
