using System.Text.Json;
using Lunate.Ai;

namespace Lunate.Coding;

public static partial class SettingsStore
{
    private static IReadOnlyDictionary<string, ModelProfile> ReadProfiles(
        JsonElement value,
        List<string> problems
    )
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            problems.Add("'profiles' must be an object");
            return new Dictionary<string, ModelProfile>(StringComparer.Ordinal);
        }

        var profiles = new Dictionary<string, ModelProfile>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            string name = property.Name;
            if (
                string.IsNullOrWhiteSpace(name)
                || name.Any(char.IsWhiteSpace)
                || name.Contains('/')
            )
            {
                problems.Add(
                    $"'profiles.{name}' must be a non-empty name without whitespace or '/'"
                );
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"'profiles.{name}' must be an object");
                continue;
            }

            string? provider = null;
            string? model = null;
            string? reasoningLevel = null;
            int? reasoningBudget = null;
            int? maxOutput = null;
            decimal? temperature = null;
            int? contextWindow = null;
            double? decisionThreshold = null;
            ModelCapabilityDescriptor? capabilities = null;
            foreach (JsonProperty field in property.Value.EnumerateObject())
            {
                switch (field.Name)
                {
                    case "provider":
                        provider = ReadProfileString(field.Value, name, field.Name, problems);
                        break;
                    case "model":
                        model = ReadProfileString(field.Value, name, field.Name, problems);
                        break;
                    case "reasoningLevel":
                        reasoningLevel = ReadProfileString(field.Value, name, field.Name, problems);
                        break;
                    case "reasoningBudget":
                        reasoningBudget = ReadProfileInt(field.Value, name, field.Name, problems);
                        break;
                    case "maxOutput":
                        maxOutput = ReadProfileInt(field.Value, name, field.Name, problems);
                        break;
                    case "temperature":
                        temperature = ReadProfileDecimal(field.Value, name, field.Name, problems);
                        break;
                    case "contextWindow":
                        contextWindow = ReadProfileInt(field.Value, name, field.Name, problems);
                        break;
                    case "decisionThreshold":
                        decisionThreshold = ReadProfileDouble(
                            field.Value,
                            name,
                            field.Name,
                            problems
                        );
                        break;
                    case "capabilities":
                        capabilities = ReadProfileCapabilities(field.Value, name, problems);
                        break;
                    default:
                        problems.Add($"unknown key 'profiles.{name}.{field.Name}'");
                        break;
                }
            }

            if (provider is null)
            {
                problems.Add($"'profiles.{name}.provider' must be a non-empty string");
            }

            if (model is null)
            {
                problems.Add($"'profiles.{name}.model' must be a non-empty string");
            }

            if (provider is not null && model is not null)
            {
                profiles.Add(
                    name,
                    new ModelProfile(
                        name,
                        provider,
                        model,
                        reasoningLevel,
                        reasoningBudget,
                        maxOutput,
                        temperature,
                        contextWindow,
                        decisionThreshold,
                        capabilities
                    )
                );
            }
        }

        return profiles;
    }

    private static IReadOnlyDictionary<string, string> ReadUsages(
        JsonElement value,
        List<string> problems
    )
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            problems.Add("'usages' must be an object");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var usages = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (
                property.Name is not ("chat" or "fast" or "decision" or "subagents" or "compaction")
            )
            {
                problems.Add($"unknown usage 'usages.{property.Name}'");
                continue;
            }

            if (
                property.Value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(property.Value.GetString())
            )
            {
                problems.Add($"'usages.{property.Name}' must be a non-empty profile name");
                continue;
            }

            usages.Add(property.Name, property.Value.GetString()!);
        }

        return usages;
    }

    private static string? ReadProfileString(
        JsonElement value,
        string profile,
        string field,
        List<string> problems
    )
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            problems.Add($"'profiles.{profile}.{field}' must be a non-empty string");
            return null;
        }

        return value.GetString();
    }

    private static int? ReadProfileInt(
        JsonElement value,
        string profile,
        string field,
        List<string> problems
    )
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number))
        {
            problems.Add($"'profiles.{profile}.{field}' must be an integer");
            return null;
        }

        return number;
    }

    private static decimal? ReadProfileDecimal(
        JsonElement value,
        string profile,
        string field,
        List<string> problems
    )
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out decimal number))
        {
            problems.Add($"'profiles.{profile}.{field}' must be a finite number");
            return null;
        }

        return number;
    }

    private static double? ReadProfileDouble(
        JsonElement value,
        string profile,
        string field,
        List<string> problems
    )
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number))
        {
            problems.Add($"'profiles.{profile}.{field}' must be a finite number");
            return null;
        }

        return number;
    }
}
