using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed class ExtensionSettingsStore : IExtensionSettings
{
    private readonly JsonElement _root;

    private ExtensionSettingsStore(JsonElement root) => _root = root;

    public static ExtensionSettingsStore Load(
        ExtensionHostOptions options,
        ExtensionDescriptor descriptor
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(descriptor);

        string path = Path.Combine(options.SettingsPath, descriptor.Id + ".json");
        JsonElement settings = JsonFiles.ReadObject(path, $"extension '{descriptor.Id}': settings");
        Validate(descriptor, settings);
        return new ExtensionSettingsStore(settings);
    }

    public bool TryGet(string path, out JsonElement value)
    {
        value = default;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        JsonElement current = _root;
        foreach (string segment in path.Split('.'))
        {
            if (
                current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(segment, out current)
            )
            {
                return false;
            }
        }

        value = current;
        return true;
    }

    private static void Validate(ExtensionDescriptor descriptor, JsonElement settings)
    {
        JsonElement? schema = descriptor.Manifest.SettingsSchema;
        if (schema is null)
        {
            return;
        }

        ValidateRequired(descriptor, schema.Value, settings);
        ValidateProperties(descriptor, schema.Value, settings);
    }

    private static void ValidateRequired(
        ExtensionDescriptor descriptor,
        JsonElement schema,
        JsonElement settings
    )
    {
        if (
            !schema.TryGetProperty("required", out JsonElement required)
            || required.ValueKind != JsonValueKind.Array
        )
        {
            return;
        }

        foreach (JsonElement name in required.EnumerateArray())
        {
            if (
                name.ValueKind == JsonValueKind.String
                && !settings.TryGetProperty(name.GetString()!, out _)
            )
            {
                throw new ExtensionLoadException(
                    $"extension '{descriptor.Id}': setting '{name.GetString()}' is required."
                );
            }
        }
    }

    private static void ValidateProperties(
        ExtensionDescriptor descriptor,
        JsonElement schema,
        JsonElement settings
    )
    {
        if (
            !schema.TryGetProperty("properties", out JsonElement properties)
            || properties.ValueKind != JsonValueKind.Object
        )
        {
            return;
        }

        foreach (JsonProperty property in properties.EnumerateObject())
        {
            if (!settings.TryGetProperty(property.Name, out JsonElement value))
            {
                continue;
            }

            if (
                !property.Value.TryGetProperty("type", out JsonElement type)
                || type.ValueKind != JsonValueKind.String
            )
            {
                continue;
            }

            string declared = type.GetString()!;
            if (!MatchesType(declared, value))
            {
                throw new ExtensionLoadException(
                    $"extension '{descriptor.Id}': setting '{property.Name}' must be {declared}."
                );
            }
        }
    }

    private static bool MatchesType(string type, JsonElement value) =>
        type switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            _ => true,
        };
}
