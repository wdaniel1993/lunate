using System.Globalization;
using System.Text.Json;

namespace Lunate.Tui;

/// <summary>
/// Pure per-tool summarizer for a tool call's argument JSON: one short, human-readable line for the
/// block header. Unknown shapes fall back to the first string value, then to the raw text. A
/// <c>read</c> call carries its line range — from the call's <c>offset</c>/<c>limit</c> when given,
/// else from the result's line count.
/// </summary>
internal static class ToolArgsSummary
{
    private const int BashCommandLimit = 60;
    private const string Elision = "\u2026";
    private const string AsciiElision = "...";
    private const string EmptyFileText = "[empty file]";

    public static string Summarize(
        string? toolName,
        string? args,
        string? output = null,
        string? home = null,
        int pathBudget = PathText.HeaderBudget,
        bool unicode = true
    )
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

        // Bash commands are not paths, however many separators they contain; a path-shaped
        // summary — and always a read/edit/write one — shortens to the header budget.
        if (pathBudget > 0 && toolName != "bash" && (IsPathTool(toolName) || LooksLikePath(line)))
        {
            line = PathText.Shorten(line, pathBudget, home, unicode);
        }

        if (toolName == "read" && ReadRange(root, output) is { Length: > 0 } range)
        {
            return line + " " + range;
        }

        if (toolName == "bash" && line.Length > BashCommandLimit)
        {
            string elision = unicode ? Elision : AsciiElision;
            return string.Concat(line.AsSpan(0, BashCommandLimit - elision.Length), elision);
        }

        return line;
    }

    private static bool IsPathTool(string? toolName) => toolName is "read" or "write" or "edit";

    private static bool LooksLikePath(string value) =>
        value.Contains('/') || value.Contains('\\') || value.StartsWith('~');

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

    /// <summary>
    /// The parenthesised line range for a <c>read</c> block: <c>(start-end)</c> when the call or
    /// (via the result's line count) the range is known, else <c>(N lines)</c>, else empty.
    /// </summary>
    private static string ReadRange(JsonElement root, string? output)
    {
        int? offset = IntegerField(root, "offset");
        int? limit = IntegerField(root, "limit");
        if (offset is null && limit is null)
        {
            int total = ResultLineCount(output);
            return total > 0 ? LineCountText(total) : string.Empty;
        }

        int start = offset ?? 1;
        int? span = limit;
        if (span is null && ResultLineCount(output) is var counted && counted > 0)
        {
            span = counted;
        }

        if (span is not { } lines || lines < 1)
        {
            return string.Empty;
        }

        return string.Create(CultureInfo.InvariantCulture, $"({start}-{start + lines - 1})");
    }

    private static string LineCountText(int count) =>
        count == 1 ? "(1 line)" : string.Create(CultureInfo.InvariantCulture, $"({count} lines)");

    /// <summary>The result's content lines: the continue hint after a paged read does not count.</summary>
    internal static int ResultLineCount(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return 0;
        }

        string text = TrimSingleTrailingNewline(output);
        if (text.Length == 0 || text == EmptyFileText)
        {
            return 0;
        }

        string[] lines = text.Split('\n');
        int count = lines.Length;
        if (count > 0)
        {
            string last = lines[count - 1].TrimEnd('\r');
            if (
                last.StartsWith("[lines ", StringComparison.Ordinal)
                && last.EndsWith("]", StringComparison.Ordinal)
            )
            {
                count--;
            }
        }

        return count;
    }

    private static int? IntegerField(JsonElement root, string name)
    {
        if (
            root.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out int value)
        )
        {
            return value;
        }

        return null;
    }

    private static string TrimSingleTrailingNewline(string text)
    {
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return text[..^2];
        }

        return text.EndsWith('\n') ? text[..^1] : text;
    }

    private static string FirstLine(string text)
    {
        int index = text.IndexOf('\n');
        string line = index < 0 ? text : text[..index];
        return line.EndsWith('\r') ? line[..^1] : line;
    }
}
