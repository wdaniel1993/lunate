using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunate.Coding;

/// <summary>
/// The <c>providers</c> settings section: user-declared OpenAI-compatible endpoints keyed by
/// provider id. Only the endpoint, the model id and the catalog name are stored here; the key
/// lives in the auth store under the same id.
/// </summary>
public static partial class SettingsStore
{
    private static IReadOnlyList<CustomProvider> ReadProviders(
        JsonElement value,
        List<string> problems
    )
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            problems.Add("'providers' must be an object");
            return [];
        }

        List<CustomProvider> providers = [];
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"'providers.{property.Name}' must be an object");
                continue;
            }

            string? baseUrl = null;
            string? model = null;
            string? catalogName = null;
            foreach (JsonProperty field in property.Value.EnumerateObject())
            {
                switch (field.Name)
                {
                    case "baseUrl":
                        baseUrl = ReadProviderField(
                            field.Value,
                            property.Name,
                            "baseUrl",
                            problems
                        );
                        break;
                    case "model":
                        model = ReadProviderField(field.Value, property.Name, "model", problems);
                        break;
                    case "catalogName":
                        catalogName = ReadProviderField(
                            field.Value,
                            property.Name,
                            "catalogName",
                            problems
                        );
                        break;
                    default:
                        problems.Add($"unknown key 'providers.{property.Name}.{field.Name}'");
                        break;
                }
            }

            if (
                !CustomProvider.TryCreate(
                    property.Name,
                    baseUrl,
                    model,
                    catalogName,
                    out CustomProvider? provider,
                    out string? error
                ) || provider is null
            )
            {
                problems.Add($"'providers.{property.Name}': {error}");
                continue;
            }

            providers.Add(provider);
        }

        return providers;
    }

    private static string? ReadProviderField(
        JsonElement value,
        string id,
        string field,
        List<string> problems
    )
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            problems.Add($"'providers.{id}.{field}' must be a string");
            return null;
        }

        return value.GetString();
    }

    /// <summary>
    /// Writes one custom provider through read-modify-write: every other settings value (including
    /// other providers) is preserved, the result is validated by the same parser, and the file is
    /// replaced atomically with <c>schemaVersion: 2</c>. On any failure the old file is untouched.
    /// </summary>
    public static void WriteProvider(CustomProvider provider, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        string path = filePath ?? DefaultFilePath();
        lock (Gate)
        {
            JsonObject root = ReadRoot(path);
            root["schemaVersion"] = SupportedSchemaVersion;
            JsonObject providers = root["providers"] as JsonObject ?? new JsonObject();
            providers[provider.Id] = new JsonObject
            {
                ["baseUrl"] = provider.BaseUrl.OriginalString,
                ["model"] = provider.Model,
                ["catalogName"] = provider.CatalogName,
            };
            root["providers"] = providers;

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
