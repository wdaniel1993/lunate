using System.Collections.Frozen;
using System.Text.Json;

namespace Lunate.Extensibility.Abstractions;

// The configurable manifest fields beyond the declaration arrays (settingsSchema aside, which stays
// in ExtensionManifest.cs by age, not structure). Each function parses exactly one top-level field
// and refuses with a message that starts with the manifest file name; unknown fields stay ignored.
public sealed partial record ExtensionManifest
{
    /// <summary>
    /// The optional <c>builtin</c> provenance assertion: defaults to <see langword="false"/>, and a
    /// non-boolean value is refused because a discovered manifest claiming the field is a loader
    /// decision, not a spelling the parser can guess at.
    /// </summary>
    private static bool OptionalBuiltIn(JsonElement root, string fileName)
    {
        if (!root.TryGetProperty("builtin", out JsonElement element))
        {
            return false;
        }

        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"{fileName}: field 'builtin' must be true or false.");
        }

        return element.GetBoolean();
    }

    /// <summary>
    /// The optional <c>workspaceServices</c> list: each entry declares a per-workspace service the
    /// extension registers by name, with an optional positive <c>maxLoadedWorkspaces</c> cap (2 when
    /// absent — the shared default lives on <see cref="ExtensionWorkspaceService"/>). Duplicate
    /// names are refused the way every declaration array is.
    /// </summary>
    private static IReadOnlyList<ExtensionWorkspaceService> WorkspaceServiceArray(
        JsonElement root,
        string fileName
    )
    {
        if (!root.TryGetProperty("workspaceServices", out JsonElement element))
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"{fileName}: field 'workspaceServices' must be an array of objects."
            );
        }

        List<ExtensionWorkspaceService> services = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"{fileName}: field 'workspaceServices' must be an array of objects."
                );
            }

            string name = RequiredServiceName(item, fileName);
            if (item.TryGetProperty("maxLoadedWorkspaces", out JsonElement maximum))
            {
                services.Add(
                    new ExtensionWorkspaceService(
                        name,
                        RequiredMaxLoadedWorkspaces(maximum, fileName, name)
                    )
                );
            }
            else
            {
                services.Add(new ExtensionWorkspaceService(name));
            }
        }

        IEnumerable<string> duplicates = services
            .GroupBy(service => service.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        if (duplicates.FirstOrDefault() is { } duplicate)
        {
            throw new InvalidDataException(
                $"{fileName}: field 'workspaceServices' declares '{duplicate}' more than once."
            );
        }

        return services;
    }

    private static string RequiredServiceName(JsonElement item, string fileName)
    {
        if (
            !item.TryGetProperty("name", out JsonElement name)
            || name.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(name.GetString())
        )
        {
            throw new InvalidDataException(
                $"{fileName}: field 'workspaceServices': service 'name' must be a non-empty string."
            );
        }

        return name.GetString()!;
    }

    private static int RequiredMaxLoadedWorkspaces(
        JsonElement element,
        string fileName,
        string name
    )
    {
        if (
            element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out int value)
            || value < 1
        )
        {
            throw new InvalidDataException(
                $"{fileName}: field 'workspaceServices' service '{name}': maxLoadedWorkspaces must be a positive integer."
            );
        }

        return value;
    }

    /// <summary>
    /// The optional <c>toolActivation</c> object: a rule per declared tool (or <c>*</c> for the
    /// default rule) from the closed condition set. The parser is the single source of the rules;
    /// the manifest stores exactly what it parsed. Keys outside <c>*</c> and the declared tools are
    /// refused here because <c>tools</c> has already been read when this runs.
    /// </summary>
    private static IReadOnlyDictionary<string, ExtensionToolActivationRule> ToolActivationObject(
        JsonElement root,
        string fileName,
        IReadOnlyList<string> tools
    )
    {
        if (!root.TryGetProperty("toolActivation", out JsonElement element))
        {
            return FrozenDictionary<string, ExtensionToolActivationRule>.Empty;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"{fileName}: field 'toolActivation' must be an object."
            );
        }

        Dictionary<string, ExtensionToolActivationRule> rules = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (rules.ContainsKey(property.Name))
            {
                throw new InvalidDataException(
                    $"{fileName}: field 'toolActivation' declares '{property.Name}' more than once."
                );
            }

            if (property.Name != "*" && !tools.Contains(property.Name))
            {
                throw new InvalidDataException(
                    $"{fileName}: field 'toolActivation' declares a rule for '{property.Name}', which is not a declared tool."
                );
            }

            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"{fileName}: field 'toolActivation' rule for '{property.Name}' must be an object."
                );
            }

            rules.Add(property.Name, RequiredRule(property.Name, property.Value, fileName));
        }

        return rules;
    }

    private static ExtensionToolActivationRule RequiredRule(
        string key,
        JsonElement rule,
        string fileName
    )
    {
        if (
            !rule.TryGetProperty("when", out JsonElement when)
            || when.ValueKind is not JsonValueKind.String
            || string.IsNullOrEmpty(when.GetString())
        )
        {
            throw new InvalidDataException(
                $"{fileName}: field 'toolActivation' rule for '{key}': a rule must declare 'when'."
            );
        }

        string? condition =
            when.ValueKind is JsonValueKind.String ? when.GetString() : null;
        return condition switch
        {
            ExtensionToolActivationRule.Always => NoAdditionalFields(
                key,
                ExtensionToolActivationRule.Always,
                rule,
                fileName
            ),
            ExtensionToolActivationRule.WorkspaceIsGitRepository => NoAdditionalFields(
                key,
                ExtensionToolActivationRule.WorkspaceIsGitRepository,
                rule,
                fileName
            ),
            ExtensionToolActivationRule.WorkspaceRootHasFile => RequiredPatterns(
                key,
                rule,
                fileName
            ),
            { } unknown => throw new InvalidDataException(
                $"{fileName}: field 'toolActivation' rule for '{key}': unknown condition '{unknown}'; the supported conditions are always, workspace-root-has-file and workspace-is-git-repository."
            ),
            _ => throw new InvalidDataException(
                $"{fileName}: field 'toolActivation' rule for '{key}': a rule must declare 'when'."
            ),
        };
    }

    private static ExtensionToolActivationRule NoAdditionalFields(
        string key,
        string condition,
        JsonElement rule,
        string fileName
    )
    {
        foreach (JsonProperty property in rule.EnumerateObject())
        {
            if (property.Name != "when")
            {
                throw new InvalidDataException(
                    $"{fileName}: field 'toolActivation' rule for '{key}': the '{condition}' condition takes no additional fields."
                );
            }
        }

        return new ExtensionToolActivationRule(condition, []);
    }

    private static ExtensionToolActivationRule RequiredPatterns(
        string key,
        JsonElement rule,
        string fileName
    )
    {
        if (
            !rule.TryGetProperty("patterns", out JsonElement patterns)
            || patterns.ValueKind != JsonValueKind.Array
            || patterns.GetArrayLength() == 0
        )
        {
            throw new InvalidDataException(
                $"{fileName}: field 'toolActivation' rule for '{key}': patterns must be a non-empty array of file names."
            );
        }

        List<string> names = [];
        foreach (JsonElement pattern in patterns.EnumerateArray())
        {
            if (
                pattern.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(pattern.GetString())
            )
            {
                throw new InvalidDataException(
                    $"{fileName}: field 'toolActivation' rule for '{key}': patterns must be a non-empty array of file names."
                );
            }

            string name = pattern.GetString()!;
            if (name.Contains('/') || name.Contains('\\'))
            {
                throw new InvalidDataException(
                    $"{fileName}: field 'toolActivation' rule for '{key}': pattern '{name}' contains a path separator; patterns match direct children only."
                );
            }

            names.Add(name);
        }

        foreach (JsonProperty property in rule.EnumerateObject())
        {
            if (property.Name is not ("when" or "patterns"))
            {
                throw new InvalidDataException(
                    $"{fileName}: field 'toolActivation' rule for '{key}': unknown field '{property.Name}' for the '{ExtensionToolActivationRule.WorkspaceRootHasFile}' condition."
                );
            }
        }

        return new ExtensionToolActivationRule(
            ExtensionToolActivationRule.WorkspaceRootHasFile,
            names
        );
    }
}
