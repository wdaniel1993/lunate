using System.Globalization;

namespace Lunate.Tui;

/// <summary>One rendered output line; <see cref="IsMarker"/> marks the hidden-lines elision.</summary>
internal readonly record struct OutputExcerptLine(string Text, bool IsMarker);

/// <summary>
/// How a tool output is bounded: first-and-last lines (default), tail-only (bash) or unfetched
/// (<see cref="ExcerptMode.Full"/>, the transcript viewer's whole output).
/// </summary>
internal enum ExcerptMode
{
    HeadAndTail,
    TailOnly,
    Full,
}

/// <summary>
/// Bounds a tool output with an explicit hidden-lines marker: the first and last lines, or the tail
/// only. A single trailing newline is not content; blank output yields no lines. The marker text —
/// the hidden-line count and the <c>ctrl+o</c> affordance — lives here, not in the renderer. Pure.
/// </summary>
internal static class OutputExcerpt
{
    public const int FirstLines = 8;
    public const int LastLines = 8;
    public const int TailLines = 5;
    public const int WholeLineLimit = FirstLines + LastLines + 1;

    public static IReadOnlyList<OutputExcerptLine> Build(
        string? output,
        ExcerptMode mode = ExcerptMode.HeadAndTail,
        bool unicode = true
    )
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

        return mode switch
        {
            ExcerptMode.Full => Whole(lines),
            ExcerptMode.TailOnly => Tail(lines, unicode),
            _ => lines.Length <= WholeLineLimit ? Whole(lines) : Elided(lines, unicode),
        };
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

    private static List<OutputExcerptLine> Tail(string[] lines, bool unicode)
    {
        if (lines.Length <= TailLines)
        {
            return Whole(lines);
        }

        var result = new List<OutputExcerptLine>(TailLines + 1);
        for (var index = lines.Length - TailLines; index < lines.Length; index++)
        {
            result.Add(new OutputExcerptLine(lines[index], false));
        }

        result.Add(new OutputExcerptLine(Marker(lines.Length - TailLines, unicode), true));
        return result;
    }

    private static List<OutputExcerptLine> Elided(string[] lines, bool unicode)
    {
        var result = new List<OutputExcerptLine>(WholeLineLimit);
        int hidden = lines.Length - FirstLines - LastLines;
        for (var index = 0; index < FirstLines; index++)
        {
            result.Add(new OutputExcerptLine(lines[index], false));
        }

        result.Add(new OutputExcerptLine(Marker(hidden, unicode), true));
        for (var index = lines.Length - LastLines; index < lines.Length; index++)
        {
            result.Add(new OutputExcerptLine(lines[index], false));
        }

        return result;
    }

    /// <summary>The hidden-line count and the <c>ctrl+o</c> affordance; ASCII when Unicode is off.</summary>
    private static string Marker(int hiddenLines, bool unicode) =>
        unicode
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"\u2026 +{hiddenLines} lines \u00b7 ctrl+o"
            )
            : string.Create(CultureInfo.InvariantCulture, $"... +{hiddenLines} lines - ctrl+o");

    private static string TrimSingleTrailingNewline(string output)
    {
        if (output.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return output[..^2];
        }

        return output.EndsWith('\n') ? output[..^1] : output;
    }
}
