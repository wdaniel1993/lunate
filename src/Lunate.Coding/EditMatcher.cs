using System.Globalization;

namespace Lunate.Coding;

/// <summary>An indent-tier match: the window's start index and the uniform prefix to re-indent with.</summary>
internal readonly record struct IndentMatch(int Start, string Prefix);

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
            if (IndentPrefix(lines, pattern, start) is { } prefix)
            {
                matches.Add(new IndentMatch(start, prefix));
            }
        }

        return matches;
    }

    public static string[] IndentReplacement(string[] newLines, string prefix) =>
        [.. newLines.Select(line => string.IsNullOrWhiteSpace(line) ? line : prefix + line)];

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

        return header + "\n" + string.Join("\n", fileLines.Skip(start).Take(oldLines.Length));
    }

    private static string? IndentPrefix(string[] lines, string[] pattern, int start)
    {
        string? prefix = null;
        var anyNonBlank = false;
        for (var offset = 0; offset < pattern.Length; offset++)
        {
            var fileLine = lines[start + offset];
            var patternLine = pattern[offset];
            if (string.IsNullOrWhiteSpace(patternLine))
            {
                if (!string.IsNullOrWhiteSpace(fileLine))
                {
                    return null;
                }

                continue;
            }

            var trimmed = patternLine.TrimEnd();
            if (!fileLine.EndsWith(trimmed, StringComparison.Ordinal))
            {
                return null;
            }

            var candidate = fileLine[..^trimmed.Length];
            if (candidate.Length == 0 || !string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            if (prefix is not null && prefix != candidate)
            {
                return null;
            }

            prefix = candidate;
            anyNonBlank = true;
        }

        return anyNonBlank ? prefix : null;
    }

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
