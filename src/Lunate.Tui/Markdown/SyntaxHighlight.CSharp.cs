namespace Lunate.Tui;

internal static partial class SyntaxHighlight
{
    private static readonly HashSet<string> CSharpKeywords = Words(
        "abstract as async await base bool break byte case catch char checked class const continue decimal",
        "default delegate do double else enum event explicit extern false finally fixed float for foreach",
        "get goto if implicit in init int interface internal is lock long nameof namespace new null object",
        "operator out override params partial private protected public readonly record ref return sbyte sealed",
        "set short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked",
        "unsafe ushort using var virtual void volatile when where while yield"
    );

    public static IReadOnlyList<HighlightSpan> CSharp(string line, ref bool inBlockComment)
    {
        var spans = new List<HighlightSpan>();
        int i = 0;
        while (i < line.Length)
        {
            char current = line[i];

            if (inBlockComment)
            {
                int close = line.IndexOf("*/", i, StringComparison.Ordinal);
                if (close < 0)
                {
                    Add(spans, line, i, line.Length, HighlightKind.Comment);
                    i = line.Length;
                }
                else
                {
                    Add(spans, line, i, close + 2, HighlightKind.Comment);
                    i = close + 2;
                    inBlockComment = false;
                }

                continue;
            }

            if (current == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                Add(spans, line, i, line.Length, HighlightKind.Comment);
                i = line.Length;
                continue;
            }

            if (current == '/' && i + 1 < line.Length && line[i + 1] == '*')
            {
                int close = line.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    Add(spans, line, i, line.Length, HighlightKind.Comment);
                    inBlockComment = true;
                    i = line.Length;
                }
                else
                {
                    Add(spans, line, i, close + 2, HighlightKind.Comment);
                    i = close + 2;
                }

                continue;
            }

            if (current == '@' && i + 1 < line.Length && line[i + 1] == '"')
            {
                i = ScanVerbatimString(line, spans, i);
                continue;
            }

            if (current == '$' && i + 1 < line.Length && line[i + 1] == '"')
            {
                i = ScanQuotedString(line, spans, i);
                continue;
            }

            if (
                (current == '$' || current == '@')
                && i + 2 < line.Length
                && line[i + 1] is '$' or '@'
                && line[i + 2] == '"'
            )
            {
                i = ScanVerbatimString(line, spans, i);
                continue;
            }

            if (current == '"')
            {
                i = ScanQuotedString(line, spans, i);
                continue;
            }

            if (char.IsLetter(current) || current == '_')
            {
                int start = i;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
                {
                    i++;
                }

                string word = line[start..i];
                Add(
                    spans,
                    line,
                    start,
                    i,
                    CSharpKeywords.Contains(word) ? HighlightKind.Keyword : HighlightKind.Plain
                );
                continue;
            }

            Add(spans, line, i, i + 1, HighlightKind.Plain);
            i++;
        }

        return spans;
    }

    private static int ScanQuotedString(string line, List<HighlightSpan> spans, int start)
    {
        int i = start + (line[start] == '"' ? 1 : 2);
        while (i < line.Length)
        {
            if (line[i] == '\\')
            {
                i += 2;
            }
            else if (line[i] == '"')
            {
                i++;
                break;
            }
            else
            {
                i++;
            }
        }

        int end = Math.Min(i, line.Length);
        Add(spans, line, start, end, HighlightKind.String);
        return end;
    }

    private static int ScanVerbatimString(string line, List<HighlightSpan> spans, int start)
    {
        int quote =
            start
            + (
                line[start] == '"' ? 0
                : line[start + 1] == '"' ? 1
                : 2
            );
        int i = quote + 1;
        while (i < line.Length)
        {
            if (line[i] == '"')
            {
                if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    i += 2;
                }
                else
                {
                    i++;
                    break;
                }
            }
            else
            {
                i++;
            }
        }

        int end = Math.Min(i, line.Length);
        Add(spans, line, start, end, HighlightKind.String);
        return end;
    }

    private static HashSet<string> Words(params string[] groups) =>
        new(
            groups.SelectMany(static group =>
                group.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            ),
            StringComparer.Ordinal
        );
}
