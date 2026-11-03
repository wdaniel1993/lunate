using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding;

public static partial class SettingsStore
{
    /// <summary>
    /// The chat usage's profile reasoning level as a session thinking level: the profile is the
    /// default for every session that runs on it, so <see cref="ApplyLaunch"/> seeds it under the
    /// launch flag and over the global <c>thinking</c> key, which keeps filling sessions whose
    /// profile is without a reasoning setting. Only <c>low</c>/<c>medium</c>/<c>high</c> seed — a
    /// profile has no <c>off</c> — and an unparseable literal seeds nothing (profile resolution
    /// names it instead). A profile <c>reasoningBudget</c> is not the session default: the
    /// request-time parameter is the level, so the budget stays a validated configuration fact.
    /// </summary>
    internal static ThinkingLevel? SeedReasoningLevel(AgentSettings settings)
    {
        if (
            !settings.Usages.TryGetValue("chat", out string? chat)
            || chat.Length == 0
            || !settings.Profiles.TryGetValue(chat, out ModelProfile? profile)
        )
        {
            return null;
        }

        return profile.ReasoningLevel switch
        {
            "low" => ThinkingLevel.Low,
            "medium" => ThinkingLevel.Medium,
            "high" => ThinkingLevel.High,
            _ => null,
        };
    }

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
            ModelCapabilityDescriptor? capabilities = null;
            string? apiFamily = null;
            string? description = null;
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
                    case "capabilities":
                        capabilities = ReadProfileCapabilities(field.Value, name, problems);
                        break;
                    case "apiFamily":
                        apiFamily = ReadProfileFamily(field.Value, name, problems);
                        break;
                    case "description":
                        description = ReadProfileString(field.Value, name, field.Name, problems);
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
                        capabilities,
                        apiFamily,
                        description
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
            if (property.Name is not ("chat" or "fast" or "subagents" or "compaction"))
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

    /// <summary>
    /// The profile's API-family override. A value that does not name a known family is refused by
    /// name with the supported set, so a typo never silently resolves to another endpoint.
    /// </summary>
    private static string? ReadProfileFamily(
        JsonElement value,
        string profile,
        List<string> problems
    )
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            problems.Add("'profiles." + profile + ".apiFamily' must be a non-empty string");
            return null;
        }

        string family = value.GetString()!;
        if (!ProviderApiFamilies.TryParse(family, out _))
        {
            problems.Add(
                $"'profiles.{profile}.apiFamily' is '{family}'; "
                    + $"supported families: {ProviderApiFamilies.Supported()}"
            );
            return null;
        }

        return family;
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
}
