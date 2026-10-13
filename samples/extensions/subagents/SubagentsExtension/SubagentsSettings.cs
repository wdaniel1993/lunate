using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace SubagentsExtension;

/// <summary>The extension's settings: the optional default step ceiling and description override.</summary>
public sealed record SubagentsSettings(int? DefaultMaxSteps, string? Description)
{
    public static SubagentsSettings From(IExtensionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        int? defaultMaxSteps = null;
        if (
            settings.TryGet("defaultMaxSteps", out JsonElement steps)
            && steps.ValueKind == JsonValueKind.Number
            && steps.TryGetInt32(out int parsed)
            && parsed >= 1
        )
        {
            defaultMaxSteps = parsed;
        }

        string? description =
            settings.TryGet("description", out JsonElement text)
            && text.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(text.GetString())
                ? text.GetString()
                : null;
        return new SubagentsSettings(defaultMaxSteps, description);
    }

    /// <summary>The default tool description; a settings override wins.</summary>
    public string ResolvedDescription =>
        Description
        ?? "Runs a subagent on a prompt and returns its final answer. The subagent runs in the same workspace with the same tools (narrowed by the optional tools allow-list) and cannot spawn further subagents.";
}
