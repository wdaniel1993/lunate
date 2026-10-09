using System.Text.Json;

namespace Lunate.Tui;

/// <summary>
/// Pure per-tool summarizer for a tool call's argument JSON: one short, human-readable line for the
/// block header. Unknown shapes fall back to the first string value, then to the raw text.
/// </summary>
internal static class ToolArgsSummary
{
    private const int BashCommandLimit = 60;
    private const string Elision = "\u2026";

    public static string Summarize(string? toolName, string? args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return string.Empty;
        }

        if (!TryParseObject(args, out var root))
        {
            return args.Trim();
        }

        string? value = ToolField(toolName, root) ?? FirstStringValue(root);
        if (value is null)
        {
            return args.Trim();
        }

        string line = FirstLine(value).Trim();
        if (line.Length == 0)
        {
            return args.Trim();
        }

        if (toolName == "bash" && line.Length > BashCommandLimit)
        {
            return string.Concat(line.AsSpan(0, BashCommandLimit - 1), Elision);
        }

        return line;
    }

    private static bool TryParseObject(string args, out JsonElement root)
    {
        try
        {
            using var document = JsonDocument.Parse(args);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                root = default;
                return false;
            }

            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            root = default;
            return false;
        }
    }

    private static string? ToolField(string? toolName, JsonElement root)
    {
        ReadOnlySpan<string> keys = toolName switch
        {
            "read" or "write" or "edit" => ["path"],
            "bash" => ["command"],
            _ when toolName?.StartsWith("cs_", StringComparison.Ordinal) == true =>
            [
                "symbol",
                "name",
                "file",
            ],
            _ => [],
        };

        foreach (string key in keys)
        {
            if (
                root.TryGetProperty(key, out var property)
                && property.ValueKind == JsonValueKind.String
            )
            {
                return property.GetString();
            }
        }

        return null;
    }

    private static string? FirstStringValue(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static string FirstLine(string text)
    {
        int index = text.IndexOf('\n');
        string line = index < 0 ? text : text[..index];
        return line.EndsWith('\r') ? line[..^1] : line;
    }
}
