using Spectre.Console;

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
/// insertions green, with a leading <c>match: &lt;tier&gt;</c> label (normalized is flagged yellow).
/// Malformed input is tolerated; an empty diff renders nothing. Pure parsing, no throwing.
/// </summary>
internal static class DiffRenderer
{
    private static readonly SpanStyle Added = new(Color: "green");
    private static readonly SpanStyle Removed = new(Color: "red");
    private static readonly SpanStyle Hunk = new(TextStyle.Dim, "cyan");
    private static readonly SpanStyle Normalized = new(Color: "yellow");

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

    public static void Append(List<StyledLine> lines, ToolDiffInfo diff)
    {
        foreach (var diffLine in Parse(diff))
        {
            var line = new StyledLine();
            line.Add(diffLine.Text, StyleFor(diffLine.Kind, diff.MatchTier));
            lines.Add(line);
        }
    }

    private static SpanStyle StyleFor(DiffLineKind kind, string tier) =>
        kind switch
        {
            DiffLineKind.MatchTier => TierStyle(tier),
            DiffLineKind.FileHeader => SpanStyle.Dim,
            DiffLineKind.HunkHeader => Hunk,
            DiffLineKind.Insertion => Added,
            DiffLineKind.Deletion => Removed,
            _ => SpanStyle.Dim,
        };

    private static SpanStyle TierStyle(string tier) =>
        tier switch
        {
            "exact" => SpanStyle.Dim,
            "normalized" => Normalized,
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
