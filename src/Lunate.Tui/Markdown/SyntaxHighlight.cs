namespace Lunate.Tui;

internal enum HighlightKind
{
    Plain,
    Keyword,
    String,
    Comment,
    Number,
    Key,
    Variable,
}

internal readonly record struct HighlightSpan(string Text, HighlightKind Kind);

internal static partial class SyntaxHighlight
{
    public static string? LanguageKey(string? info)
    {
        string name = (info ?? string.Empty).Trim().ToLowerInvariant();
        return name switch
        {
            "csharp" or "cs" => "csharp",
            "json" => "json",
            "sh" or "bash" or "shell" or "zsh" => "shell",
            _ => null,
        };
    }

    public static IReadOnlyList<HighlightSpan> Line(
        string language,
        string line,
        ref bool inBlockComment
    )
    {
        return language switch
        {
            "csharp" => CSharp(line, ref inBlockComment),
            "json" => Json(line),
            "shell" => Shell(line),
            _ => [new HighlightSpan(line, HighlightKind.Plain)],
        };
    }

    public static IReadOnlyList<HighlightSpan> Json(string line)
    {
        var spans = new List<HighlightSpan>();
        int i = 0;
        while (i < line.Length)
        {
            char current = line[i];

            if (current == '"')
            {
                int end = ScanJsonString(line, i);
                int lookahead = end;
                while (lookahead < line.Length && line[lookahead] == ' ')
                {
                    lookahead++;
                }

                bool isKey = lookahead < line.Length && line[lookahead] == ':';
                Add(spans, line, i, end, isKey ? HighlightKind.Key : HighlightKind.String);
                i = end;
                continue;
            }

            if (current == '-' || char.IsDigit(current))
            {
                int start = i;
                i++;
                while (
                    i < line.Length
                    && (char.IsDigit(line[i]) || line[i] is '.' or 'e' or 'E' or '+' or '-')
                )
                {
                    i++;
                }

                Add(spans, line, start, i, HighlightKind.Number);
                continue;
            }

            Add(spans, line, i, i + 1, HighlightKind.Plain);
            i++;
        }

        return spans;
    }

    private static int ScanJsonString(string line, int start)
    {
        int i = start + 1;
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

        return Math.Min(i, line.Length);
    }

    private static void Add(
        List<HighlightSpan> spans,
        string line,
        int start,
        int end,
        HighlightKind kind
    )
    {
        if (end <= start)
        {
            return;
        }

        string text = line[start..end];
        if (spans.Count > 0 && spans[^1].Kind == kind)
        {
            spans[^1] = spans[^1] with { Text = spans[^1].Text + text };
        }
        else
        {
            spans.Add(new HighlightSpan(text, kind));
        }
    }
}
