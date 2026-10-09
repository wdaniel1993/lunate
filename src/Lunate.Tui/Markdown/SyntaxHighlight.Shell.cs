namespace Lunate.Tui;

internal static partial class SyntaxHighlight
{
    private static readonly HashSet<string> ShellKeywords = Words(
        "alias awk bash case cat cd cp curl declare do done echo elif else esac exit export fi for if in",
        "local make mkdir mv pwd read readonly return rm set sh source sudo test then unset until while zsh"
    );

    public static IReadOnlyList<HighlightSpan> Shell(string line)
    {
        var spans = new List<HighlightSpan>();
        int i = 0;
        while (i < line.Length)
        {
            char current = line[i];

            if (current == '#' && (i == 0 || char.IsWhiteSpace(line[i - 1])))
            {
                Add(spans, line, i, line.Length, HighlightKind.Comment);
                break;
            }

            if (current == '\'')
            {
                int close = line.IndexOf('\'', i + 1);
                int end = close < 0 ? line.Length : close + 1;
                Add(spans, line, i, end, HighlightKind.String);
                i = end;
                continue;
            }

            if (current == '"')
            {
                int end = ScanQuotedString(line, spans, i);
                i = end;
                continue;
            }

            if (current == '$')
            {
                int end = ScanVariable(line, i);
                Add(spans, line, i, end, HighlightKind.Variable);
                i = end;
                continue;
            }

            if (char.IsLetter(current) || current == '_')
            {
                int start = i;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] is '_' or '-'))
                {
                    i++;
                }

                string word = line[start..i];
                Add(
                    spans,
                    line,
                    start,
                    i,
                    ShellKeywords.Contains(word) ? HighlightKind.Keyword : HighlightKind.Plain
                );
                continue;
            }

            Add(spans, line, i, i + 1, HighlightKind.Plain);
            i++;
        }

        return spans;
    }

    private static int ScanVariable(string line, int start)
    {
        int i = start + 1;
        if (i >= line.Length)
        {
            return i;
        }

        char current = line[i];
        if (current == '{')
        {
            int close = line.IndexOf('}', i + 1);
            return close < 0 ? line.Length : close + 1;
        }

        if (char.IsLetter(current) || current == '_')
        {
            i++;
            while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
            {
                i++;
            }

            return i;
        }

        if (char.IsDigit(current) || current is '@' or '?' or '#' or '$' or '!' or '*')
        {
            return i + 1;
        }

        return start + 1;
    }
}
