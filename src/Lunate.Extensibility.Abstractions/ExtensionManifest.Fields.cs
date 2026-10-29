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
}
