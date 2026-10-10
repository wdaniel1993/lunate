using System.Globalization;

namespace Lunate.Coding;

/// <summary>An indent-tier match: the window's start index and the leading-whitespace offset to re-indent with.</summary>
internal readonly record struct IndentMatch(int Start, int Offset);

/// <summary>
/// Window matching for the edit tool: the exact, normalized and indent tiers, plus the
/// not-found hints (whitespace-significant refusal and closest region).
/// </summary>
internal static class EditMatcher
{
    private static readonly string[] WhitespaceSignificantExtensions =
    [
        ".py",
        ".yaml",
        ".yml",
        ".mk",
    ];

    public static bool IsWhitespaceSignificant(string path)
    {
        var extension = Path.GetExtension(path);
        if (WhitespaceSignificantExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return Path.GetFileName(path).Equals("Makefile", StringComparison.OrdinalIgnoreCase);
    }

    public static List<int> FindMatches(string[] lines, string[] pattern, bool normalized)
    {
        var matches = new List<int>();
        if (pattern.Length > lines.Length)
        {
            return matches;
        }

        for (var start = 0; start <= lines.Length - pattern.Length; start++)
        {
            var matched = true;
            for (var offset = 0; offset < pattern.Length; offset++)
            {
                if (!LineEquals(lines[start + offset], pattern[offset], normalized))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                matches.Add(start);
            }
        }

        return matches;
    }

    public static List<IndentMatch> FindIndentMatches(string[] lines, string[] pattern)
    {
        var matches = new List<IndentMatch>();
        if (pattern.Length > lines.Length)
        {
            return matches;
        }

        for (var start = 0; start <= lines.Length - pattern.Length; start++)
        {
            if (IndentOffset(lines, pattern, start) is { } offset)
            {
                matches.Add(new IndentMatch(start, offset));
            }
        }

        return matches;
    }

    public static string[] IndentReplacement(string[] newLines, int offset)
    {
        if (offset == 0)
        {
            return newLines;
        }

        var result = new string[newLines.Length];
        for (var index = 0; index < newLines.Length; index++)
        {
            var line = newLines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                result[index] = line;
            }
            else if (offset > 0)
            {
                result[index] = new string(' ', offset) + line;
            }
            else
            {
                result[index] = line[Math.Min(LeadingWhitespace(line), -offset)..];
            }
        }

        return result;
    }

    public static string Ambiguity(List<int> matches, string relativePath)
    {
        var lines = string.Join(
            ", ",
            matches.Select(match => (match + 1).ToString(CultureInfo.InvariantCulture))
        );

        return string.Create(
            CultureInfo.InvariantCulture,
            $"old_text matches {matches.Count} places in {relativePath} at lines {lines}; make it longer so it matches once"
        );
    }

    public static string NotFound(string[] fileLines, string[] oldLines, string relativePath)
    {
        if (IsWhitespaceSignificant(relativePath))
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"could not find old_text in {relativePath}; the indent tier is disabled for whitespace-significant files \u2014 re-read the file and edit with the exact text"
            );
        }

        var (score, start) = ClosestWindow(fileLines, oldLines);
        if (score < 1)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"could not find old_text in {relativePath}"
            );
        }

        var header = string.Create(
            CultureInfo.InvariantCulture,
            $"could not find old_text in {relativePath}; closest region (lines {start + 1}-{start + oldLines.Length}):"
        );

        var region = fileLines.Skip(start).Take(oldLines.Length).Select(StripCarriageReturn);

        return header + "\n" + string.Join("\n", region);
    }

    private static int? IndentOffset(string[] lines, string[] pattern, int start)
    {
        int? offset = null;
        for (var index = 0; index < pattern.Length; index++)
        {
            var patternLine = pattern[index];
            var fileLine = lines[start + index];
            if (!string.Equals(patternLine.Trim(), fileLine.Trim(), StringComparison.Ordinal))
            {
                return null;
            }

            if (offset is null && !string.IsNullOrWhiteSpace(patternLine))
            {
                offset = LeadingWhitespace(fileLine) - LeadingWhitespace(patternLine);
            }
        }

        return offset;
    }

    private static int LeadingWhitespace(string line)
    {
        var count = 0;
        while (count < line.Length && char.IsWhiteSpace(line[count]))
        {
            count++;
        }

        return count;
    }

    private static string StripCarriageReturn(string line) =>
        line.EndsWith('\r') ? line[..^1] : line;

    private static (int Score, int Start) ClosestWindow(string[] fileLines, string[] oldLines)
    {
        var best = (Score: 0, Start: 0);
        if (oldLines.Length > fileLines.Length)
        {
            return best;
        }

        for (var start = 0; start <= fileLines.Length - oldLines.Length; start++)
        {
            var score = 0;
            for (var offset = 0; offset < oldLines.Length; offset++)
            {
                if (fileLines[start + offset].TrimEnd() == oldLines[offset].TrimEnd())
                {
                    score++;
                }
            }

            if (score > best.Score)
            {
                best = (score, start);
            }
        }

        return best;
    }

    private static bool LineEquals(string line, string patternLine, bool normalized) =>
        normalized ? line.TrimEnd() == patternLine.TrimEnd() : line == patternLine;
}
