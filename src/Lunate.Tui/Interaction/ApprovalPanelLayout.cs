using System.Text;

namespace Lunate.Tui;

internal enum ApprovalTone
{
    Plain,
    Warning,
    Muted,
    Accent,
}

internal readonly record struct ApprovalSpan(string Text, ApprovalTone Tone);

internal enum ApprovalPart
{
    Headline,
    Detail,
    Question,
}

internal sealed record ApprovalPanelLine(IReadOnlyList<ApprovalSpan> Spans, ApprovalPart Part)
{
    public string Text => string.Concat(Spans.Select(span => span.Text));
}

/// <summary>
/// The approval panel's pure layout: a warning headline naming the tool and its action, a muted
/// detail block prepared and bounded by the Coding builder, which the layout only indents and
/// clips (only its first line in the compact band), and the question with its choices. Spans carry
/// tones only; the markup renderer and the live frame map them to their own colour forms. Widths
/// are terminal cells; clipping keeps whole runes, like <see cref="StyledLine.Clip"/>.
/// </summary>
internal static class ApprovalPanelLayout
{
    private const string UnicodeGlyph = "\u26a0";
    private const string AsciiGlyph = "!";

    public static IReadOnlyList<ApprovalPanelLine> Build(
        ApprovalPromptModel prompt,
        int width,
        bool unicode
    )
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var lines = new List<ApprovalPanelLine>
        {
            Clip(
                [new ApprovalSpan(Headline(prompt, unicode), ApprovalTone.Warning)],
                ApprovalPart.Headline,
                width
            ),
        };
        AppendDetail(lines, prompt, width, compact: WidthBands.IsCompact(width));
        AppendQuestion(lines, prompt, width);
        return lines;
    }

    private static string Headline(ApprovalPromptModel prompt, bool unicode)
    {
        string tool = CellText.SingleLine(prompt.ToolName);
        string action = Action(prompt, unicode);
        string glyph = unicode ? UnicodeGlyph : AsciiGlyph;
        return action.Length > 0 ? $"{glyph} {tool}  {action}" : $"{glyph} {tool}";
    }

    private static string Action(ApprovalPromptModel prompt, bool unicode) =>
        CellText.SingleLine(
            prompt.Detail is { Count: > 0 } detail
                ? detail[0]
                : ToolArgsSummary.Summarize(prompt.ToolName, prompt.ArgsSummary, unicode: unicode)
        );

    private static void AppendDetail(
        List<ApprovalPanelLine> lines,
        ApprovalPromptModel prompt,
        int width,
        bool compact
    )
    {
        foreach (string line in DetailBody(prompt, compact))
        {
            lines.Add(
                Clip(
                    [new ApprovalSpan("  " + line, ApprovalTone.Muted)],
                    ApprovalPart.Detail,
                    width
                )
            );
        }

        if (WorkspaceRoot(prompt) is { Length: > 0 } root)
        {
            lines.Add(
                Clip(
                    [new ApprovalSpan("  in " + root, ApprovalTone.Muted)],
                    ApprovalPart.Detail,
                    width
                )
            );
        }
    }

    /// <summary>
    /// The block below the headline, prepared and bounded by the Coding builder, whose marker is
    /// part of it. The layout does not bound it again; it only normalizes each line to one
    /// physical line and indents it. In the compact band the block drops to its first line; the
    /// workspace root stays.
    /// </summary>
    private static IReadOnlyList<string> DetailBody(ApprovalPromptModel prompt, bool compact)
    {
        if (prompt.Detail is not { Count: > 1 } detail)
        {
            return [];
        }

        return compact
            ? [CellText.SingleLine(detail[1])]
            : [.. detail.Skip(1).Select(CellText.SingleLine)];
    }

    private static string WorkspaceRoot(ApprovalPromptModel prompt) =>
        prompt.WorkspaceRoot is { Length: > 0 } root ? CellText.SingleLine(root) : string.Empty;

    private static void AppendQuestion(
        List<ApprovalPanelLine> lines,
        ApprovalPromptModel prompt,
        int width
    )
    {
        string tool = CellText.SingleLine(prompt.ToolName);
        var question = new List<ApprovalSpan>();
        if (
            prompt.Asker is { Length: > 0 } rawAsker
            && CellText.SingleLine(rawAsker) is { Length: > 0 } asker
        )
        {
            question.Add(new ApprovalSpan(asker, ApprovalTone.Plain));
            question.Add(new ApprovalSpan(" asks: ", ApprovalTone.Plain));
        }

        question.Add(new ApprovalSpan("Allow ", ApprovalTone.Plain));
        question.Add(new ApprovalSpan(tool, ApprovalTone.Plain));
        question.Add(new ApprovalSpan("?", ApprovalTone.Warning));

        IReadOnlyList<ApprovalSpan> choices = Choices();
        if (Width(question) + 2 + Width(choices) <= width)
        {
            question.Add(new ApprovalSpan("  ", ApprovalTone.Plain));
            question.AddRange(choices);
            lines.Add(new ApprovalPanelLine(question, ApprovalPart.Question));
            return;
        }

        // The choices never clip: a narrow frame puts the question above them instead.
        lines.Add(Clip(question, ApprovalPart.Question, width));
        lines.Add(new ApprovalPanelLine(choices, ApprovalPart.Question));
    }

    private static IReadOnlyList<ApprovalSpan> Choices() =>
        [
            new("[", ApprovalTone.Plain),
            new("y", ApprovalTone.Accent),
            new("]es  [", ApprovalTone.Plain),
            new("n", ApprovalTone.Accent),
            new("]o  [", ApprovalTone.Plain),
            new("a", ApprovalTone.Accent),
            new("]lways this session", ApprovalTone.Plain),
        ];

    private static ApprovalPanelLine Clip(
        IReadOnlyList<ApprovalSpan> spans,
        ApprovalPart part,
        int width
    )
    {
        if (width == int.MaxValue)
        {
            return new ApprovalPanelLine(spans, part);
        }

        var clipped = new List<ApprovalSpan>(spans.Count);
        if (width <= 0)
        {
            return new ApprovalPanelLine(clipped, part);
        }

        var used = 0;
        foreach (var span in spans)
        {
            var kept = new StringBuilder(span.Text.Length);
            foreach (var rune in span.Text.EnumerateRunes())
            {
                int runeWidth = CellText.Width(rune);
                if (used + runeWidth > width)
                {
                    if (kept.Length > 0)
                    {
                        clipped.Add(new ApprovalSpan(kept.ToString(), span.Tone));
                    }

                    return new ApprovalPanelLine(clipped, part);
                }

                kept.Append(rune);
                used += runeWidth;
            }

            if (kept.Length > 0)
            {
                clipped.Add(new ApprovalSpan(kept.ToString(), span.Tone));
            }
        }

        return new ApprovalPanelLine(clipped, part);
    }

    private static int Width(IReadOnlyList<ApprovalSpan> spans) =>
        spans.Sum(span => CellText.Width(span.Text));
}
