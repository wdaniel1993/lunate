using System.Globalization;

namespace Lunate.Tui;

/// <summary>
/// Parses a <c>cs_*</c> tool result's own summary: the severity counts line and the first few
/// diagnostic lines after it (capped, each truncated). Pure; output without a summary line
/// contributes nothing.
/// </summary>
internal static class CsToolMeta
{
    private const int TopItems = 2;
    private const int ItemLimit = 80;
    private const string CleanPrefix = "No compiler errors or warnings";

    public static void Append(
        string? output,
        List<string> parts,
        bool unicode = true,
        bool compact = false
    )
    {
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        string[] lines = output.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (ParseCounts(lines[index].TrimEnd('\r').Trim()) is not { } counts)
            {
                continue;
            }

            if (counts.Errors > 0 || counts.Warnings > 0)
            {
                parts.Add(CountsText(counts));
            }

            // The compact header has no room for the top items: the counts are the whole meta.
            if (!compact)
            {
                AppendItems(lines, index + 1, parts, unicode);
            }

            return;
        }
    }

    private static void AppendItems(string[] lines, int start, List<string> parts, bool unicode)
    {
        var items = 0;
        for (var index = start; index < lines.Length && items < TopItems; index++)
        {
            string text = lines[index].TrimEnd('\r').Trim();
            if (!IsDiagnostic(text))
            {
                continue;
            }

            parts.Add(Truncate(text, unicode));
            items++;
        }
    }

    /// <summary>The tool's counts line: <c>N errors, M warnings in …</c> or its clean form.</summary>
    private static (int Errors, int Warnings)? ParseCounts(string line)
    {
        if (line.StartsWith(CleanPrefix, StringComparison.Ordinal))
        {
            return (0, 0);
        }

        int cursor = Digits(line, 0);
        if (cursor == 0 || !line.AsSpan(cursor).StartsWith(" error", StringComparison.Ordinal))
        {
            return null;
        }

        int errors = int.Parse(line.AsSpan(0, cursor), CultureInfo.InvariantCulture);
        cursor += " error".Length;
        if (cursor < line.Length && line[cursor] == 's')
        {
            cursor++;
        }

        if (!line.AsSpan(cursor).StartsWith(", ", StringComparison.Ordinal))
        {
            return null;
        }

        cursor += 2;
        int start = cursor;
        cursor = Digits(line, cursor);
        if (
            cursor == start
            || !line.AsSpan(cursor).StartsWith(" warning", StringComparison.Ordinal)
        )
        {
            return null;
        }

        int warnings = int.Parse(line.AsSpan(start, cursor - start), CultureInfo.InvariantCulture);
        return (errors, warnings);
    }

    private static int Digits(string text, int start)
    {
        var index = start;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            index++;
        }

        return index;
    }

    private static string CountsText((int Errors, int Warnings) counts) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{counts.Errors} error{(counts.Errors == 1 ? "" : "s")} "
                + $"{counts.Warnings} warning{(counts.Warnings == 1 ? "" : "s")}"
        );

    private static bool IsDiagnostic(string line) =>
        line.Contains("): error ", StringComparison.Ordinal)
        || line.Contains("): warning ", StringComparison.Ordinal);

    private static string Truncate(string item, bool unicode) =>
        item.Length <= ItemLimit
            ? item
            : string.Concat(
                item.AsSpan(0, ItemLimit - (unicode ? 1 : 3)),
                unicode ? "\u2026" : "..."
            );
}
