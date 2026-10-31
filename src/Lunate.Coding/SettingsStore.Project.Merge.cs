using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding;

public static partial class SettingsStore
{
    private static AgentSettings MergeProject(
        AgentSettings user,
        AgentSettings project,
        IReadOnlySet<string> providerNames,
        string projectPath
    )
    {
        var profiles = user.Profiles.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.Ordinal
        );
        foreach ((string name, ModelProfile overlay) in project.Profiles)
        {
            if (overlay.CapabilitiesOverride is not null)
            {
                throw ProjectProblem(projectPath, $"profiles.{name}.capabilities is user-only");
            }

            if (!providerNames.Contains(overlay.Provider))
            {
                throw ProjectProblem(
                    projectPath,
                    $"profiles.{name}.provider '{overlay.Provider}' does not name a user provider"
                );
            }

            if (user.Profiles.TryGetValue(name, out ModelProfile? existing))
            {
                EnsureProjectLimitDoesNotRise(
                    projectPath,
                    name,
                    "maxOutput",
                    existing.MaxOutput,
                    overlay.MaxOutput
                );
                EnsureProjectLimitDoesNotRise(
                    projectPath,
                    name,
                    "reasoningBudget",
                    existing.ReasoningBudget,
                    overlay.ReasoningBudget
                );
                EnsureProjectLimitDoesNotRise(
                    projectPath,
                    name,
                    "contextWindow",
                    existing.ContextWindow,
                    overlay.ContextWindow
                );
            }

            profiles[name] = MergeProfile(user.Profiles.GetValueOrDefault(name), overlay);
        }

        var usages = new Dictionary<string, string>(user.Usages, StringComparer.Ordinal);
        foreach ((string usage, string profile) in project.Usages)
        {
            usages[usage] = profile;
        }

        foreach ((string usage, string profile) in usages)
        {
            if (!profiles.ContainsKey(profile))
            {
                throw ProjectProblem(
                    projectPath,
                    $"usages.{usage} references missing profile '{profile}'"
                );
            }
        }

        (int outputLimit, int spillThreshold) = MergeOutputLimits(user, project, projectPath);
        SubagentLimits subagents = MergeSubagentLimits(
            user.Subagents,
            project.Subagents,
            projectPath
        );
        return user with
        {
            Profiles = profiles,
            Usages = usages,
            ToolOutputLimit = outputLimit,
            SpillThresholdTokens = spillThreshold,
            Subagents = subagents,
        };
    }

    private static ModelProfile MergeProfile(ModelProfile? existing, ModelProfile overlay) =>
        existing is null
            ? overlay
            : overlay with
            {
                ReasoningLevel = overlay.ReasoningLevel ?? existing.ReasoningLevel,
                ReasoningBudget = overlay.ReasoningBudget ?? existing.ReasoningBudget,
                MaxOutput = overlay.MaxOutput ?? existing.MaxOutput,
                Temperature = overlay.Temperature ?? existing.Temperature,
                ContextWindow = overlay.ContextWindow ?? existing.ContextWindow,
                CapabilitiesOverride =
                    overlay.CapabilitiesOverride ?? existing.CapabilitiesOverride,
            };

    private static void EnsureProjectLimitDoesNotRise(
        string path,
        string profile,
        string field,
        int? user,
        int? project
    )
    {
        if (user is int userLimit && project is int projectLimit && projectLimit > userLimit)
        {
            throw ProjectProblem(
                path,
                $"profiles.{profile}.{field} cannot raise the user limit {userLimit}"
            );
        }
    }

    private static (int Output, int Spill) MergeOutputLimits(
        AgentSettings user,
        AgentSettings project,
        string path
    )
    {
        if (!TryReadProjectSection(path, "output", out JsonElement output))
        {
            return (user.ToolOutputLimit, user.SpillThresholdTokens);
        }

        int outputLimit = user.ToolOutputLimit;
        int spillThreshold = user.SpillThresholdTokens;
        foreach (JsonProperty property in output.EnumerateObject())
        {
            int value = property.Value.GetInt32();
            if (property.Name == "toolOutputLimit")
            {
                if (value > user.ToolOutputLimit)
                {
                    throw ProjectProblem(
                        path,
                        "output.toolOutputLimit cannot raise the user limit"
                    );
                }

                outputLimit = project.ToolOutputLimit;
            }
            else if (property.Name == "spillThresholdTokens")
            {
                if (value > user.SpillThresholdTokens)
                {
                    throw ProjectProblem(
                        path,
                        "output.spillThresholdTokens cannot raise the user limit"
                    );
                }

                spillThreshold = project.SpillThresholdTokens;
            }
        }

        return (outputLimit, spillThreshold);
    }

    private static SubagentLimits MergeSubagentLimits(
        SubagentLimits user,
        SubagentLimits project,
        string path
    )
    {
        if (!TryReadProjectSection(path, "subagents", out JsonElement section))
        {
            return user;
        }

        foreach (JsonProperty property in section.EnumerateObject())
        {
            switch (property.Name)
            {
                case "concurrency":
                    EnsureNotRaised(
                        path,
                        "subagents.concurrency",
                        user.Concurrency,
                        project.Concurrency
                    );
                    break;
                case "tokenBudget":
                    EnsureNotRaised(
                        path,
                        "subagents.tokenBudget",
                        user.TokenBudget,
                        project.TokenBudget
                    );
                    break;
                case "treeTokenBudget":
                    EnsureNotRaised(
                        path,
                        "subagents.treeTokenBudget",
                        user.TreeTokenBudget,
                        project.TreeTokenBudget
                    );
                    break;
            }
        }

        return new SubagentLimits(
            section.TryGetProperty("concurrency", out _) ? project.Concurrency : user.Concurrency,
            section.TryGetProperty("tokenBudget", out _) ? project.TokenBudget : user.TokenBudget,
            section.TryGetProperty("treeTokenBudget", out _)
                ? project.TreeTokenBudget
                : user.TreeTokenBudget
        );
    }

    private static void EnsureNotRaised<T>(string path, string field, T? user, T? project)
        where T : struct, IComparable<T>
    {
        if (
            user is T userLimit
            && (project is not T projectLimit || projectLimit.CompareTo(userLimit) > 0)
        )
        {
            throw ProjectProblem(path, $"{field} cannot raise the user limit");
        }
    }

    private static bool TryReadProjectSection(string path, string name, out JsonElement section)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.TryGetProperty(name, out JsonElement value))
        {
            section = value.Clone();
            return true;
        }

        section = default;
        return false;
    }

    private static InvalidDataException ProjectProblem(string path, string problem) =>
        new($"Invalid project settings in '{path}': {problem}. No settings were applied.");
}
