namespace Lunate.Tui;

internal enum DiffLineKind
{
    MatchTier,
    FileHeader,
    HunkHeader,
    Insertion,
    Deletion,
    Context,
}

internal readonly record struct DiffLine(DiffLineKind Kind, string Text);

/// <summary>
/// Parses the unified diff produced by <c>Lunate.Coding</c>'s <c>LineDiff</c> (and any other
/// unified text) into styled lines: file headers and context dim, hunks cyan-dim, deletions red,
/// insertions green, with a leading <c>match: &lt;tier&gt;</c> label (normalized and indent are flagged yellow).
/// Malformed input is tolerated; an empty diff renders nothing. Pure parsing, no throwing.
/// </summary>
internal static class DiffRenderer
{
    public static IReadOnlyList<DiffLine> Parse(ToolDiffInfo diff)
    {
        if (string.IsNullOrWhiteSpace(diff.UnifiedDiff))
        {
            return [];
        }

        var lines = new List<DiffLine>();
        if (!string.IsNullOrWhiteSpace(diff.MatchTier))
        {
            lines.Add(new DiffLine(DiffLineKind.MatchTier, "match: " + diff.MatchTier));
        }

        bool inHunk = false;
        foreach (string raw in TrimSingleTrailingNewline(diff.UnifiedDiff).Split('\n'))
        {
            string text = raw.EndsWith('\r') ? raw[..^1] : raw;
            DiffLineKind kind;
            if (!inHunk && IsFileHeader(text))
            {
                kind = DiffLineKind.FileHeader;
            }
            else if (text.StartsWith("@@", StringComparison.Ordinal))
            {
                kind = DiffLineKind.HunkHeader;
                inHunk = true;
            }
            else if (inHunk && text.StartsWith('+'))
            {
                kind = DiffLineKind.Insertion;
            }
            else if (inHunk && text.StartsWith('-'))
            {
                kind = DiffLineKind.Deletion;
            }
            else
            {
                kind = DiffLineKind.Context;
            }

            lines.Add(new DiffLine(kind, text));
        }

        return lines;
    }

    public static void Append(
        List<StyledLine> lines,
        ToolDiffInfo diff,
        TerminalCapabilities capabilities
    )
    {
        foreach (var diffLine in Parse(diff))
        {
            var line = new StyledLine();
            line.Add(diffLine.Text, StyleFor(diffLine.Kind, diff.MatchTier, capabilities));
            lines.Add(line);
        }
    }

    /// <summary>The unified diff's changed-line counts; file headers and hunk headers do not count.</summary>
    public static (int Added, int Removed) ChangeCounts(ToolDiffInfo diff)
    {
        var added = 0;
        var removed = 0;
        foreach (var line in Parse(diff))
        {
            if (line.Kind == DiffLineKind.Insertion)
            {
                added++;
            }
            else if (line.Kind == DiffLineKind.Deletion)
            {
                removed++;
            }
        }

        return (added, removed);
    }

    private static SpanStyle StyleFor(
        DiffLineKind kind,
        string tier,
        TerminalCapabilities capabilities
    ) =>
        kind switch
        {
            DiffLineKind.MatchTier => TierStyle(tier, capabilities),
            DiffLineKind.FileHeader => SpanStyle.Dim,
            // A hunk header keeps its dim attribute and takes the accent's colour.
            DiffLineKind.HunkHeader => capabilities.Span(ThemeToken.Accent) with
            {
                Text = TextStyle.Dim,
            },
            DiffLineKind.Insertion => capabilities.Span(ThemeToken.DiffAdd),
            DiffLineKind.Deletion => capabilities.Span(ThemeToken.DiffRemove),
            _ => SpanStyle.Dim,
        };

    private static SpanStyle TierStyle(string tier, TerminalCapabilities capabilities) =>
        tier switch
        {
            "exact" => SpanStyle.Dim,
            "normalized" or "indent" => capabilities.Span(ThemeToken.Warning),
            _ => SpanStyle.Plain,
        };

    private static bool IsFileHeader(string text) =>
        text.StartsWith("--- ", StringComparison.Ordinal)
        || text.StartsWith("+++ ", StringComparison.Ordinal);

    private static string TrimSingleTrailingNewline(string text)
    {
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return text[..^2];
        }

        return text.EndsWith('\n') ? text[..^1] : text;
    }
}
