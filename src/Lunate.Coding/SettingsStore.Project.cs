using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding;

public static partial class SettingsStore
{
    internal static string ProjectFilePath(string workspaceRoot) =>
        Path.Combine(Path.GetFullPath(workspaceRoot), ".lunate", "settings.json");

    internal static AgentSettings ResolveWithProject(
        string? userSettingsPath,
        string? projectSettingsPath,
        IReadOnlyList<ProviderEntry> userProviders,
        Func<string, string?>? environment = null
    )
    {
        ArgumentNullException.ThrowIfNull(userProviders);
        if (projectSettingsPath is null || !File.Exists(projectSettingsPath))
        {
            return Resolve(userSettingsPath, environment);
        }

        ValidateProjectScope(projectSettingsPath);
        AgentSettings user = Resolve(userSettingsPath, environment);
        var problems = new List<string>();
        AgentSettings project = ParseFile(projectSettingsPath, problems);
        if (problems.Count > 0)
        {
            throw new InvalidDataException(
                $"Invalid project settings in '{projectSettingsPath}': {string.Join("; ", problems)}. No settings were applied."
            );
        }

        return MergeProject(
            user,
            project,
            userProviders
                .Select(provider => provider.Name)
                .Concat(user.Providers.Select(provider => provider.Id))
                .ToHashSet(StringComparer.Ordinal),
            projectSettingsPath
        );
    }

    internal static void ValidateProjectScope(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Invalid project settings in '{path}': the file is not valid JSON ({exception.Message}). No settings were applied.",
                exception
            );
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"Invalid project settings in '{path}': the root must be an object. No settings were applied."
                );
            }

            var problems = new List<string>();
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Name == "providers")
                {
                    FindProviderDefinitionKeys(property.Value, "providers", problems);
                    continue;
                }

                if (
                    property.Name
                    is not ("schemaVersion" or "profiles" or "usages" or "output" or "subagents")
                )
                {
                    problems.Add($"project settings key '{property.Name}' is not allowed");
                    continue;
                }

                FindForbiddenProjectKeys(property.Value, property.Name, problems);
            }

            if (problems.Count > 0)
            {
                throw new InvalidDataException(
                    $"Invalid project settings in '{path}': {string.Join("; ", problems)}. Project settings may reference user providers and profiles or lower limits only. No settings were applied."
                );
            }
        }

        var parseProblems = new List<string>();
        _ = ParseFile(path, parseProblems);
        if (parseProblems.Count > 0)
        {
            throw new InvalidDataException(
                $"Invalid project settings in '{path}': {string.Join("; ", parseProblems)}. No settings were applied."
            );
        }
    }

    private static void FindProviderDefinitionKeys(
        JsonElement value,
        string path,
        List<string> problems
    )
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"project settings key '{path}' is forbidden; providers are user-scoped");
            return;
        }

        foreach (JsonProperty provider in value.EnumerateObject())
        {
            string providerPath = $"{path}.{provider.Name}";
            if (provider.Value.ValueKind != JsonValueKind.Object)
            {
                problems.Add(
                    $"project settings key '{providerPath}' is forbidden; providers are user-scoped"
                );
                continue;
            }

            foreach (JsonProperty field in provider.Value.EnumerateObject())
            {
                problems.Add(
                    $"project settings key '{providerPath}.{field.Name}' is forbidden; providers are user-scoped"
                );
            }

            if (provider.Value.EnumerateObject().Any() is false)
            {
                problems.Add(
                    $"project settings key '{providerPath}' is forbidden; providers are user-scoped"
                );
            }
        }
    }

    private static void FindForbiddenProjectKeys(
        JsonElement value,
        string path,
        List<string> problems
    )
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in value.EnumerateObject())
            {
                string propertyPath = $"{path}.{property.Name}";
                if (
                    property.Name
                    is "baseUrl"
                        or "endpoint"
                        or "credentialReference"
                        or "headers"
                        or "kind"
                )
                {
                    problems.Add(
                        $"project settings key '{propertyPath}' is forbidden; providers are user-scoped"
                    );
                }
                else
                {
                    FindForbiddenProjectKeys(property.Value, propertyPath, problems);
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in value.EnumerateArray())
            {
                FindForbiddenProjectKeys(item, $"{path}[{index}]", problems);
                index++;
            }
        }
    }
}
