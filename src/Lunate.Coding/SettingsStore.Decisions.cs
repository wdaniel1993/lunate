using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// Reads and validates the <c>decisions</c> section: one block that names the decision
/// profile and configures all four consumers. Without a profile the layer is unavailable
/// and nothing is offered — a consumer turned on without one is not an error, but
/// unknown profile names, unknown keys, unknown modes and an out-of-range threshold are.
/// </summary>
public static partial class SettingsStore
{
    private const string ConsumerModeLiterals = "'off', 'shadow', or 'enforce'";

    private static DecisionsSettings ReadDecisions(JsonElement value, List<string> problems)
    {
        var decisions = new DecisionsSettings();
        if (value.ValueKind != JsonValueKind.Object)
        {
            problems.Add("'decisions' must be an object");
            return decisions;
        }

        foreach (JsonProperty property in value.EnumerateObject())
        {
            switch (property.Name)
            {
                case "profile":
                    decisions = decisions with
                    {
                        Profile = ReadDecisionsProfile(property.Value, problems),
                    };
                    break;
                case "approvals":
                    decisions = decisions with
                    {
                        Approvals = ReadDecisionsMode(
                            property.Value,
                            "'decisions.approvals'",
                            problems
                        ),
                    };
                    break;
                case "approvalsThreshold":
                    decisions = decisions with
                    {
                        ApprovalsThreshold = ReadApprovalsThreshold(property.Value, problems),
                    };
                    break;
                case "routing":
                    decisions = decisions with
                    {
                        Routing = ReadDecisionsMode(
                            property.Value,
                            "'decisions.routing'",
                            problems
                        ),
                    };
                    break;
                case "routingProfiles":
                    decisions = decisions with
                    {
                        RoutingProfiles = ReadRoutingProfiles(property.Value, problems),
                    };
                    break;
                case "tool":
                    if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        decisions = decisions with { Tool = property.Value.GetBoolean() };
                        break;
                    }

                    problems.Add("'decisions.tool' must be true or false");
                    break;
                case "questions":
                    decisions = decisions with
                    {
                        Questions = ReadQuestionsMode(property.Value, problems),
                    };
                    break;
                default:
                    problems.Add($"unknown key 'decisions.{property.Name}'");
                    break;
            }
        }

        return decisions;
    }

    private static string? ReadDecisionsProfile(JsonElement value, List<string> problems)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            problems.Add("'decisions.profile' must be a non-empty profile name");
            return null;
        }

        return value.GetString();
    }

    private static DecisionMode ReadDecisionsMode(
        JsonElement value,
        string subject,
        List<string> problems
    )
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

        problems.Add($"{subject} must be {ConsumerModeLiterals}");
        return DecisionMode.Off;
    }

    private static double ReadApprovalsThreshold(JsonElement value, List<string> problems)
    {
        if (
            value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out double threshold)
            && double.IsFinite(threshold)
            && threshold is >= 0 and <= 1
        )
        {
            return threshold;
        }

        problems.Add("'decisions.approvalsThreshold' must be a finite number from 0 through 1");
        return DecisionsSettings.DefaultApprovalsThreshold;
    }

    private static DecisionQuestionsMode ReadQuestionsMode(JsonElement value, List<string> problems)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            switch (value.GetString())
            {
                case "off":
                    return DecisionQuestionsMode.Off;
                case "suggest":
                    return DecisionQuestionsMode.Suggest;
            }
        }

        problems.Add("'decisions.questions' must be 'off' or 'suggest'");
        return DecisionQuestionsMode.Off;
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

    /// <summary>
    /// Cross-field validation after the whole file parses: every profile the decisions block
    /// references must exist, and routing turned on needs at least two profiles to choose
    /// between (at most 255). A consumer without a decision profile stays legal.
    /// </summary>
    private static void ValidateDecisions(AgentSettings settings, List<string> problems)
    {
        if (settings.Decisions.Profile is { } profile && !settings.Profiles.ContainsKey(profile))
        {
            problems.Add($"'decisions.profile' names an unknown profile: {profile}");
        }

        if (settings.Decisions.RoutingProfiles.Any(item => !settings.Profiles.ContainsKey(item)))
        {
            string missing = string.Join(
                ", ",
                settings.Decisions.RoutingProfiles.Where(item =>
                    !settings.Profiles.ContainsKey(item)
                )
            );
            problems.Add($"'routingProfiles' names unknown profiles: {missing}");
        }

        if (
            settings.Decisions.Routing != DecisionMode.Off
            && settings.Decisions.RoutingProfiles.Count < 2
        )
        {
            problems.Add("'decisions.routing' requires at least two 'routingProfiles'");
        }

        if (settings.Decisions.RoutingProfiles.Count > 255)
        {
            problems.Add("'routingProfiles' cannot contain more than 255 profiles");
        }
    }
}
