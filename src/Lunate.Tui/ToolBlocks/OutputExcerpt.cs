using System.Globalization;

namespace Lunate.Tui;

/// <summary>One rendered output line; <see cref="IsMarker"/> marks the hidden-lines elision.</summary>
internal readonly record struct OutputExcerptLine(string Text, bool IsMarker);

/// <summary>
/// Bounds a tool output to its first and last lines with an explicit hidden-lines marker between
/// them. A single trailing newline is not content; blank output yields no lines. Pure.
/// </summary>
internal static class OutputExcerpt
{
    public const int FirstLines = 8;
    public const int LastLines = 8;
    public const int WholeLineLimit = FirstLines + LastLines + 1;

    public static IReadOnlyList<OutputExcerptLine> Build(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return [];
        }

        string text = TrimSingleTrailingNewline(output);
        if (text.Length == 0)
        {
            return [];
        }

        string[] lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            lines[index] = lines[index].TrimEnd('\r');
        }

        if (lines.Length == 1 && lines[0].Length == 0)
        {
            return [];
        }

        return lines.Length <= WholeLineLimit ? Whole(lines) : Elided(lines);
    }

    private static List<OutputExcerptLine> Whole(string[] lines)
    {
        var result = new List<OutputExcerptLine>(lines.Length);
        foreach (string line in lines)
        {
            result.Add(new OutputExcerptLine(line, false));
        }

        return result;
    }

    private static List<OutputExcerptLine> Elided(string[] lines)
    {
        var result = new List<OutputExcerptLine>(WholeLineLimit);
        int hidden = lines.Length - FirstLines - LastLines;
        for (var index = 0; index < FirstLines; index++)
        {
            result.Add(new OutputExcerptLine(lines[index], false));
        }

        result.Add(new OutputExcerptLine(Marker(hidden), true));
        for (var index = lines.Length - LastLines; index < lines.Length; index++)
        {
            result.Add(new OutputExcerptLine(lines[index], false));
        }

        return result;
    }

    private static string Marker(int hiddenLines) =>
        string.Create(CultureInfo.InvariantCulture, $"\u2026 {hiddenLines} lines hidden \u2026");

    private static string TrimSingleTrailingNewline(string output)
    {
        if (output.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return output[..^2];
        }

        return output.EndsWith('\n') ? output[..^1] : output;
    }
}
