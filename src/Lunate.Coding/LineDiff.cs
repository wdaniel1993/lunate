using System.Globalization;

namespace Lunate.Coding;

internal static class LineDiff
{
    private const int ContextLines = 3;
    private const int MaxMyersLines = 20000;

    public static string Unified(string oldText, string newText, string relativePath)
    {
        if (oldText == newText)
        {
            return string.Empty;
        }

        var oldLines = TextFile.SplitLines(oldText);
        var newLines = TextFile.SplitLines(newText);
        var edits =
            oldLines.Length > MaxMyersLines || newLines.Length > MaxMyersLines
                ? FallbackEdits(oldLines, newLines)
                : MyersEdits(oldLines, newLines);

        return Format(edits, relativePath);
    }

    private static List<Edit> MyersEdits(string[] oldLines, string[] newLines)
    {
        var count = oldLines.Length;
        var other = newLines.Length;
        var max = count + other;
        var offset = max;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();

        for (var distance = 0; distance <= max; distance++)
        {
            trace.Add((int[])v.Clone());
            for (var diagonal = -distance; diagonal <= distance; diagonal += 2)
            {
                int x;
                if (
                    diagonal == -distance
                    || (diagonal != distance && v[offset + diagonal - 1] < v[offset + diagonal + 1])
                )
                {
                    x = v[offset + diagonal + 1];
                }
                else
                {
                    x = v[offset + diagonal - 1] + 1;
                }

                var y = x - diagonal;
                while (x < count && y < other && oldLines[x] == newLines[y])
                {
                    x++;
                    y++;
                }

                v[offset + diagonal] = x;
                if (x >= count && y >= other)
                {
                    return Backtrack(trace, oldLines, newLines, distance, offset);
                }
            }
        }

        throw new InvalidOperationException("The diff did not converge.");
    }

    private static List<Edit> Backtrack(
        List<int[]> trace,
        string[] oldLines,
        string[] newLines,
        int distance,
        int offset
    )
    {
        var edits = new List<Edit>();
        var x = oldLines.Length;
        var y = newLines.Length;

        for (var depth = distance; depth > 0; depth--)
        {
            var v = trace[depth];
            var diagonal = x - y;
            int previousDiagonal;
            if (
                diagonal == -depth
                || (diagonal != depth && v[offset + diagonal - 1] < v[offset + diagonal + 1])
            )
            {
                previousDiagonal = diagonal + 1;
            }
            else
            {
                previousDiagonal = diagonal - 1;
            }

            var previousX = v[offset + previousDiagonal];
            var previousY = previousX - previousDiagonal;
            while (x > previousX && y > previousY)
            {
                edits.Add(new Edit(EditKind.Equal, oldLines[x - 1]));
                x--;
                y--;
            }

            if (x == previousX)
            {
                edits.Add(new Edit(EditKind.Insert, newLines[previousY]));
            }
            else
            {
                edits.Add(new Edit(EditKind.Delete, oldLines[previousX]));
            }

            x = previousX;
            y = previousY;
        }

        while (x > 0 && y > 0)
        {
            edits.Add(new Edit(EditKind.Equal, oldLines[x - 1]));
            x--;
            y--;
        }

        edits.Reverse();
        return edits;
    }

    private static List<Edit> FallbackEdits(string[] oldLines, string[] newLines)
    {
        var prefix = CommonPrefix(oldLines, newLines);
        var suffix = CommonSuffix(oldLines, newLines, prefix);
        var oldMiddle = oldLines.Length - prefix - suffix;
        var newMiddle = newLines.Length - prefix - suffix;

        var edits = new List<Edit>();
        for (var index = 0; index < prefix; index++)
        {
            edits.Add(new Edit(EditKind.Equal, oldLines[index]));
        }

        for (var index = prefix; index < prefix + oldMiddle; index++)
        {
            edits.Add(new Edit(EditKind.Delete, oldLines[index]));
        }

        for (var index = prefix; index < prefix + newMiddle; index++)
        {
            edits.Add(new Edit(EditKind.Insert, newLines[index]));
        }

        for (var index = oldLines.Length - suffix; index < oldLines.Length; index++)
        {
            edits.Add(new Edit(EditKind.Equal, oldLines[index]));
        }

        return edits;
    }

    private static int CommonPrefix(string[] oldLines, string[] newLines)
    {
        var max = Math.Min(oldLines.Length, newLines.Length);
        var count = 0;
        while (count < max && oldLines[count] == newLines[count])
        {
            count++;
        }

        return count;
    }

    private static int CommonSuffix(string[] oldLines, string[] newLines, int prefix)
    {
        var max = Math.Min(oldLines.Length, newLines.Length) - prefix;
        var count = 0;
        while (count < max && oldLines[^(count + 1)] == newLines[^(count + 1)])
        {
            count++;
        }

        return count;
    }

    private static string Format(List<Edit> edits, string relativePath)
    {
        var changed = new List<int>();
        for (var index = 0; index < edits.Count; index++)
        {
            if (edits[index].Kind != EditKind.Equal)
            {
                changed.Add(index);
            }
        }

        if (changed.Count == 0)
        {
            return string.Empty;
        }

        var output = new List<string> { $"--- a/{relativePath}", $"+++ b/{relativePath}" };

        var groupStart = changed[0];
        var groupEnd = changed[0];
        for (var index = 1; index < changed.Count; index++)
        {
            if (changed[index] - groupEnd <= 2 * ContextLines + 1)
            {
                groupEnd = changed[index];
            }
            else
            {
                AppendHunk(output, edits, groupStart, groupEnd);
                groupStart = changed[index];
                groupEnd = changed[index];
            }
        }

        AppendHunk(output, edits, groupStart, groupEnd);
        return string.Join('\n', output);
    }

    private static void AppendHunk(List<string> output, List<Edit> edits, int first, int last)
    {
        var start = Math.Max(0, first - ContextLines);
        var end = Math.Min(edits.Count, last + 1 + ContextLines);
        var oldStart = 0;
        var newStart = 0;
        for (var index = 0; index < start; index++)
        {
            if (edits[index].Kind != EditKind.Insert)
            {
                oldStart++;
            }

            if (edits[index].Kind != EditKind.Delete)
            {
                newStart++;
            }
        }

        var oldCount = 0;
        var newCount = 0;
        for (var index = start; index < end; index++)
        {
            if (edits[index].Kind != EditKind.Insert)
            {
                oldCount++;
            }

            if (edits[index].Kind != EditKind.Delete)
            {
                newCount++;
            }
        }

        var oldHeader = oldCount == 0 ? oldStart : oldStart + 1;
        var newHeader = newCount == 0 ? newStart : newStart + 1;
        output.Add(
            string.Create(
                CultureInfo.InvariantCulture,
                $"@@ -{oldHeader},{oldCount} +{newHeader},{newCount} @@"
            )
        );

        for (var index = start; index < end; index++)
        {
            var marker = edits[index].Kind switch
            {
                EditKind.Delete => '-',
                EditKind.Insert => '+',
                _ => ' ',
            };
            output.Add(marker + edits[index].Line);
        }
    }

    private enum EditKind
    {
        Equal,
        Delete,
        Insert,
    }

    private readonly record struct Edit(EditKind Kind, string Line);
}
