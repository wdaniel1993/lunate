using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace McpFromExtensionExtension;

/// <summary>The extension's settings: the marker file path the fake server appends to.</summary>
public sealed record McpSettings(string? MarkerPath)
{
    public static McpSettings From(IExtensionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new McpSettings(
            settings.TryGet("markerPath", out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null
        );
    }
}
