using System.Collections.Frozen;
using System.Text.Json;

namespace Lunate.Extensibility.Abstractions;

// The activation and naming fields of the manifest (toolActivation and toolAliases), split off
// ExtensionManifest.Fields.cs to keep both files inside the size guideline. The parse is the
// single source of both tables: the manifest record stores exactly what was parsed.
public sealed partial record ExtensionManifest
{
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

        string? condition = when.ValueKind is JsonValueKind.String ? when.GetString() : null;
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

    /// <summary>
    /// The optional <c>toolAliases</c> object: a declared tool's local name mapped to a model-facing
    /// alias the registry composes from. The shape <c>[A-Za-z0-9_-]{1,64}</c>, the <c>64</c> cap the
    /// model-facing name limit imposes, and the <c>__</c> ban (it would read as an extension-id
    /// separator) are checked here so the loader never sees an alias it cannot publish.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ToolAliasesObject(
        JsonElement root,
        string fileName,
        IReadOnlyList<string> tools
    )
    {
        if (!root.TryGetProperty("toolAliases", out JsonElement element))
        {
            return FrozenDictionary<string, string>.Empty;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{fileName}: field 'toolAliases' must be an object.");
        }

        Dictionary<string, string> aliases = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (aliases.ContainsKey(property.Name))
            {
                throw new InvalidDataException(
                    $"{fileName}: field 'toolAliases' declares '{property.Name}' more than once."
                );
            }

            if (!tools.Contains(property.Name))
            {
                throw new InvalidDataException(
                    $"{fileName}: field 'toolAliases' declares an alias for '{property.Name}', which is not a declared tool."
                );
            }

            string? value =
                property.Value.ValueKind is JsonValueKind.String
                    ? property.Value.GetString()
                    : null;
            // A non-string value is shown as its JSON text; a string (even an empty one) as itself.
            string shown = value ?? property.Value.GetRawText();

            bool valid =
                value is { } alias
                && alias.Length >= 1
                && alias.Length <= 64
                && alias.All(character =>
                    character
                        is >= 'a'
                            and <= 'z'
                            or >= 'A'
                            and <= 'Z'
                            or >= '0'
                            and <= '9'
                            or '_'
                            or '-'
                )
                && !alias.Contains("__", StringComparison.Ordinal);
            if (!valid)
            {
                throw new InvalidDataException(
                    $"{fileName}: field 'toolAliases' for '{property.Name}': alias '{shown}' must be 1-64 characters of [A-Za-z0-9_-] and must not contain '__'."
                );
            }

            aliases.Add(property.Name, value!);
        }

        return aliases;
    }
}
