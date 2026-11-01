using System.Text.Json;

namespace Lunate.Coding;

/// <summary>
/// One accepted settings key with its shape, default and file scope. <see cref="Scope"/> is
/// <c>user</c> when only the user settings file may carry the key, and <c>user, project</c>
/// when a project file may carry it too.
/// </summary>
public sealed record SettingEntry(
    string Name,
    string Type,
    string Default,
    string Scope,
    IReadOnlyList<string> Allowed,
    string Description
);

/// <summary>
/// The settings schema: one entry per key <see cref="SettingsStore"/> accepts, read from
/// <c>settings-schema.json</c> — the file <c>scripts/docs-sync</c> renders docs/configuration.md's
/// table from, the mirror of how the TUI's <c>keys.json</c> backs the router and the guide's key
/// table. The file is the only place a settings key is documented; tests pin it to the parser's
/// own literals (both directions) and to the project merge's scope rules, so a key cannot be
/// accepted, documented or scoped alone. Loaded on first use, never at startup.
/// </summary>
public static class SettingsSchema
{
    internal const string ResourceName = "Lunate.Coding.settings-schema.json";

    private const int SupportedVersion = 1;

    /// <summary>The file scopes a settings key may declare.</summary>
    internal static readonly IReadOnlyList<string> Scopes = ["user", "user, project"];

    public static IReadOnlyList<SettingEntry> All { get; } = Load();

    private static IReadOnlyList<SettingEntry> Load()
    {
        using Stream stream =
            typeof(SettingsSchema).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"the settings resource '{ResourceName}' is missing from the "
                    + $"{typeof(SettingsSchema).Assembly.GetName().Name} assembly; "
                    + "settings-schema.json must be embedded (see the EmbeddedResource item in "
                    + "Lunate.Coding.csproj)"
            );

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"'{ResourceName}' is not valid JSON: {exception.Message}",
                exception
            );
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (
                root.ValueKind != JsonValueKind.Object
                || root.TryGetProperty("version", out JsonElement version) is false
                || version.GetInt32() != SupportedVersion
            )
            {
                throw new InvalidOperationException(
                    $"'{ResourceName}' must be version {SupportedVersion}, so the schema and its "
                        + "readers cannot disagree about which shape is written"
                );
            }

            if (
                root.TryGetProperty("entries", out JsonElement entries) is false
                || entries.ValueKind != JsonValueKind.Array
            )
            {
                throw new InvalidOperationException(
                    $"'{ResourceName}' has no 'entries' array; the schema is unreadable"
                );
            }

            var rows = new List<SettingEntry>();
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                rows.Add(ReadEntry(entry));
            }

            if (rows.Count == 0)
            {
                throw new InvalidOperationException(
                    $"'{ResourceName}' lists no entries; a schema without keys documents nothing"
                );
            }

            return rows;
        }
    }

    private static SettingEntry ReadEntry(JsonElement entry)
    {
        string name = Text(entry, "name") ?? throw Named(entry, "name");
        string type = Text(entry, "type") ?? throw Named(entry, "type", "'type'");
        string defaultText = Text(entry, "default") ?? throw Named(entry, "default");
        string scope = Text(entry, "scope") ?? throw Named(entry, name, "'scope'");
        if (!Scopes.Contains(scope, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{ResourceName}': entry '{name}' declares scope '{scope}'; accepted scopes are "
                    + string.Join(" or ", Scopes.Select(one => $"'{one}'"))
            );
        }

        string description =
            Text(entry, "description") ?? throw Named(entry, name, "'description'");
        var allowed = new List<string>();
        if (entry.TryGetProperty("allowed", out JsonElement values))
        {
            if (values.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    $"'{ResourceName}': entry '{name}' declares 'allowed' that is not an array"
                );
            }

            foreach (JsonElement value in values.EnumerateArray())
            {
                if (
                    value.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(value.GetString())
                )
                {
                    throw new InvalidOperationException(
                        $"'{ResourceName}': entry '{name}' lists an allowed value that is not a "
                            + "non-empty string"
                    );
                }

                allowed.Add(value.GetString()!);
            }
        }

        return new SettingEntry(name, type, defaultText, scope, allowed, description);
    }

    private static string? Text(JsonElement entry, string property)
    {
        if (
            entry.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
        )
        {
            return value.GetString();
        }

        return null;
    }

    private static InvalidOperationException Named(
        JsonElement entry,
        string name,
        string property = "'name'"
    ) => new($"'{ResourceName}': an entry is missing {property} (near '{name}')");
}
