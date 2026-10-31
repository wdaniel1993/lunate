using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding;

public static partial class SettingsStore
{
    private static void ValidateRoutingSettings(AgentSettings settings, List<string> problems)
    {
        if (settings.RoutingProfiles.Any(profile => !settings.Profiles.ContainsKey(profile)))
        {
            string missing = string.Join(
                ", ",
                settings.RoutingProfiles.Where(profile => !settings.Profiles.ContainsKey(profile))
            );
            problems.Add($"'routingProfiles' names unknown profiles: {missing}");
        }

        if (settings.RoutingMode != DecisionMode.Off && settings.RoutingProfiles.Count < 2)
        {
            problems.Add("'routingMode' requires at least two 'routingProfiles'");
        }

        if (settings.RoutingProfiles.Count > 255)
        {
            problems.Add("'routingProfiles' cannot contain more than 255 profiles");
        }
    }

    private static DecisionMode ReadRoutingMode(JsonElement value, List<string> problems)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            switch (value.GetString())
            {
                case "off":
                    return DecisionMode.Off;
                case "shadow":
                    return DecisionMode.Shadow;
                case "enforce":
                    return DecisionMode.Enforce;
            }
        }

        problems.Add("'routingMode' must be 'off', 'shadow', or 'enforce'");
        return DecisionMode.Off;
    }

    private static IReadOnlyList<string> ReadRoutingProfiles(
        JsonElement value,
        List<string> problems
    )
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            problems.Add("'routingProfiles' must be an array of profile names");
            return [];
        }

        List<string> profiles = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (
                item.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(item.GetString())
            )
            {
                problems.Add("'routingProfiles' entries must be non-empty profile names");
                continue;
            }

            string profile = item.GetString()!;
            if (!seen.Add(profile))
            {
                problems.Add($"'routingProfiles' repeats profile '{profile}'");
                continue;
            }

            profiles.Add(profile);
        }

        return profiles;
    }
}
