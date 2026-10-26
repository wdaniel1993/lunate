using System.Text.RegularExpressions;

namespace Lunate.Tui.Tests;

/// <summary>One row of the guide's key table, as written.</summary>
internal sealed record GuideKeyRow(string KeyCell, string Action, IReadOnlyList<string> Keys);

/// <summary>
/// Reads <c>docs/guide.md</c>'s key table: the table under its heading, the Key column only, cells
/// split on <c>/</c> with backticks and whitespace trimmed. A key named inside an Action cell is
/// prose, not a documented binding — the rule that stops "the text still mentions Ctrl+J" from
/// passing for a missing row. Every failure names what is missing, so a deleted table cannot pass
/// vacuously.
/// </summary>
internal static class GuideKeyTable
{
    public const string Heading = "**Key bindings (one meaning each)**";

    private static readonly Regex KeyCellCleanup = new("^[`\\s]+|[`\\s]+$", RegexOptions.Compiled);

    /// <summary>Parses the table or throws with a message naming what the guide is missing.</summary>
    public static IReadOnlyList<GuideKeyRow> Parse(string guide)
    {
        ArgumentNullException.ThrowIfNull(guide);

        string[] lines = guide.ReplaceLineEndings("\n").Split('\n');
        int heading = Array.IndexOf(lines, Heading);
        if (heading < 0)
        {
            throw new InvalidOperationException($"docs/guide.md has no '{Heading}' heading.");
        }

        int header = -1;
        for (var index = heading + 1; index < lines.Length; index++)
        {
            if (
                Cells(lines[index]) is [var first, var second, ..]
                && first == "Key"
                && second == "Action"
            )
            {
                header = index;
                break;
            }

            // The section's prose may follow the heading; the table starts at the first pipe row.
            if (lines[index].StartsWith('|'))
            {
                break;
            }
        }

        if (header < 0)
        {
            throw new InvalidOperationException(
                $"docs/guide.md has no '| Key | Action |' header under '{Heading}'."
            );
        }

        var rows = new List<GuideKeyRow>();
        for (int index = header + 1; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.Length == 0 || !line.StartsWith('|'))
            {
                break;
            }

            string[] cells = Cells(line);
            if (cells.Length < 2 || cells.All(cell => cell.Length == 0 || cell.All(c => c == '-')))
            {
                continue;
            }

            string[] keys =
            [
                .. cells[0]
                    .Split('/')
                    .Select(key => KeyCellCleanup.Replace(key, string.Empty))
                    .Where(key => key.Length > 0),
            ];
            rows.Add(new GuideKeyRow(cells[0], cells[1], keys));
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                $"docs/guide.md's '| Key | Action |' table has no key rows."
            );
        }

        return rows;
    }

    private static string[] Cells(string line) =>
        [.. line.Trim().TrimStart('|').TrimEnd('|').Split('|').Select(cell => cell.Trim())];
}
