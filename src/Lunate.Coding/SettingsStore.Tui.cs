using System.Text.Json;
using System.Text.Json.Nodes;
using Lunate.Tui;

namespace Lunate.Coding;

/// <summary>
/// The <c>tui</c> settings section: the named theme and the persisted key remaps. Every problem
/// is collected into the load's one error; a refused entry keeps the others. The writer goes
/// through the same parser, so a saved value is always one the reader accepts.
/// </summary>
public static partial class SettingsStore
{
    /// <summary>Reads the <c>tui</c> section; a missing key keeps the documented default.</summary>
    private static TuiSettings ReadTui(JsonElement value, List<string> problems)
    {
        var settings = new TuiSettings();
        if (value.ValueKind != JsonValueKind.Object)
        {
            problems.Add("'tui' must be an object");
            return settings;
        }

        foreach (JsonProperty property in value.EnumerateObject())
        {
            switch (property.Name)
            {
                case "theme":
                    settings = settings with
                    {
                        Theme = ReadTuiTheme(property.Value, problems),
                    };
                    break;
                case "keys":
                    settings = settings with
                    {
                        Keys = ReadRemaps(property.Value, problems),
                    };
                    break;
                default:
                    problems.Add($"unknown key 'tui.{property.Name}'");
                    break;
            }
        }

        return settings;
    }

    private static string ReadTuiTheme(JsonElement value, List<string> problems)
    {
        if (
            value.ValueKind == JsonValueKind.String
            && TuiTheme.TryGet(value.GetString(), out TuiTheme theme)
        )
        {
            return theme.Name;
        }

        string got =
            value.ValueKind == JsonValueKind.String ? $"but was '{value.GetString()}'" : "but was not a string";
        problems.Add($"'tui.theme' must be {TuiTheme.Literals}, {got}");
        return TuiTheme.Default.Name;
    }

    /// <summary>
    /// Reads <c>tui.keys</c>: an object of binding ids, each an array of the binding's key specs.
    /// An unknown id is refused with the closest catalogue ids named; every entry is atomic — one
    /// refused key drops the whole entry, because a remap is the binding's whole key list.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadRemaps(
        JsonElement value,
        List<string> problems
    )
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            problems.Add("'tui.keys' must be an object of binding ids");
            return new Dictionary<string, IReadOnlyList<string>>();
        }

        Dictionary<string, IReadOnlyList<string>> remaps = new(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            string id = property.Name;
            string subject = $"'tui.keys.{id}'";
            if (KeyBindingCatalog.BindingsFor(id).Count == 0)
            {
                problems.Add(
                    $"{subject} is not a binding id; the closest names are " + NearestIdList(id)
                );
                continue;
            }

            string? refused;
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                refused = $"{subject} must be an array of key specs";
            }
            else if (property.Value.GetArrayLength() == 0)
            {
                refused = $"{subject} must be a non-empty array of key specs";
            }
            else
            {
                refused = ReadKeySpecs(subject, property.Value, out List<string>? declared);
                if (declared is not null)
                {
                    remaps[id] = declared;
                }
            }

            if (refused is not null)
            {
                problems.Add(refused);
            }
        }

        return remaps;
    }

    /// <summary>The canonical key list for one entry, or the problem that dropped it.</summary>
    private static string? ReadKeySpecs(
        string subject,
        JsonElement value,
        out List<string>? declared
    )
    {
        declared = null;
        var canonical = new List<string>();
        foreach (JsonElement item in value.EnumerateArray())
        {
            string? spec =
                item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (spec is null)
            {
                return $"{subject} must be an array of key specs";
            }

            if (!KeyString.TryParse(spec, out KeyPattern pattern, out string? error))
            {
                return $"{subject} declares '{spec}': {error}";
            }

            // The canonical spelling is what the help overlay prints, so a saved remap reads
            // back as the same text a guide-table reader sees.
            canonical.Add(KeyPatternFormatter.Describe(pattern));
        }

        declared = canonical;
        return null;
    }

    private static string NearestIdList(string id) =>
        string.Join(", ", KeyBindingCatalog.NearestIds(id).Select(name => $"'{name}'"));

    /// <summary>
    /// Writes the theme and the persisted remaps through read-modify-write: every other value in
    /// the existing JSON is preserved, the result is validated by the same parser, and the file is
    /// replaced atomically with <c>schemaVersion: 2</c>. On any failure the old file is untouched.
    /// </summary>
    public static void WriteTui(TuiSettings tui, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(tui);
        string path = filePath ?? DefaultFilePath();
        lock (Gate)
        {
            JsonObject root = ReadRoot(path);
            root["schemaVersion"] = SupportedSchemaVersion;
            var section = new JsonObject { ["theme"] = tui.Theme };
            if (tui.Keys.Count > 0)
            {
                var keys = new JsonObject();
                foreach ((string id, IReadOnlyList<string> specs) in tui.Keys)
                {
                    var list = new JsonArray();
                    foreach (string spec in specs)
                    {
                        list.Add(spec);
                    }

                    keys[id] = list;
                }

                section["keys"] = keys;
            }

            root["tui"] = section;

            string json = root.ToJsonString(WriteOptions) + "\n";
            var problems = new List<string>();
            _ = ParseJson(json, problems);
            if (problems.Count > 0)
            {
                throw new InvalidDataException(
                    $"Invalid settings in '{path}': {string.Join("; ", problems)}. No settings were written."
                );
            }

            WriteAtomically(path, json);
        }
    }
}
