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
    Legend,
}

internal sealed record ApprovalPanelLine(IReadOnlyList<ApprovalSpan> Spans, ApprovalPart Part)
{
    public string Text => string.Concat(Spans.Select(span => span.Text));
}

/// <summary>
/// The approval panel's pure layout: a warning headline naming the tool and its action, a muted
/// detail block prepared and bounded by the Coding builder, which the layout only indents and
/// clips (only its first line in the compact band), and the question with its choices. The choices
/// carry a textual <c>&gt; </c> gutter on the selected one — text, not colour, so the selection
/// survives <c>NO_COLOR</c> and the ASCII fallback; below the 36 cells the inline row needs they
/// stack one per row with the gutter kept, and a label clips only after its key letter. Spans carry
/// tones only; the markup renderer and the live frame map them to their own colour forms. Widths
/// are terminal cells; clipping keeps whole runes, like <see cref="StyledLine.Clip"/>.
/// </summary>
internal static class ApprovalPanelLayout
{
    private const string UnicodeGlyph = "\u26a0";
    private const string AsciiGlyph = "!";
    private const string Marker = "> ";
    private const string Gutter = "  ";

    /// <summary>The three fixed decisions: opening bracket, key letter and the rest of the label.</summary>
    private static readonly (string Open, string Key, string Tail)[] ChoiceLabels =
    [
        ("[", "y", "]es"),
        ("[", "n", "]o"),
        ("[", "a", "]lways this session"),
    ];

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
        prompt.WorkspaceRoot is { Length: > 0 } root
            ? CellText.SingleLine(PathText.Shorten(root, int.MaxValue, PathText.Home()))
            : string.Empty;

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

        int selected = prompt.Selected;
        IReadOnlyList<ApprovalSpan> choices = InlineChoices(selected);
        if (Width(choices) <= width)
        {
            if (Width(question) + 2 + Width(choices) <= width)
            {
                question.Add(new ApprovalSpan("  ", ApprovalTone.Plain));
                question.AddRange(choices);
                lines.Add(new ApprovalPanelLine(question, ApprovalPart.Question));
            }
            else
            {
                // The choices never clip: a narrow frame puts the question above them instead.
                lines.Add(Clip(question, ApprovalPart.Question, width));
                lines.Add(new ApprovalPanelLine(choices, ApprovalPart.Question));
            }
        }
        else
        {
            // Below the 36-cell inline row the block stacks, keeping the gutter column so the
            // marker does not jump horizontally, and never clipping a label before its key letter.
            lines.Add(Clip(question, ApprovalPart.Question, width));
            for (var index = 0; index < ChoiceLabels.Length; index++)
            {
                lines.Add(
                    new ApprovalPanelLine(
                        StackedChoice(ChoiceLabels[index], index == selected, width),
                        ApprovalPart.Question
                    )
                );
            }
        }

        AppendLegend(lines, prompt, width);
    }

    /// <summary>The three choices on one row: <c>[y]es  &gt; [n]o  [a]lways this session</c>.</summary>
    private static IReadOnlyList<ApprovalSpan> InlineChoices(int selected)
    {
        var spans = new List<ApprovalSpan>();
        for (var index = 0; index < ChoiceLabels.Length; index++)
        {
            if (index > 0)
            {
                spans.Add(new ApprovalSpan(Gutter, ApprovalTone.Plain));
            }

            if (index == selected)
            {
                spans.Add(new ApprovalSpan(Marker, ApprovalTone.Plain));
            }

            spans.AddRange(ChoiceSpans(ChoiceLabels[index]));
        }

        return spans;
    }

    private static IReadOnlyList<ApprovalSpan> ChoiceSpans(
        (string Open, string Key, string Tail) label
    ) =>
        [
            new(label.Open, ApprovalTone.Plain),
            new(label.Key, ApprovalTone.Accent),
            new(label.Tail, ApprovalTone.Plain),
        ];

    /// <summary>
    /// One stacked choice, gutter included. A row too narrow for the label keeps the bracket and
    /// the key letter (<c>[a]</c>): a torn label would hide which decision the row is, and a hidden
    /// decision is the one thing this panel may not do.
    /// </summary>
    private static IReadOnlyList<ApprovalSpan> StackedChoice(
        (string Open, string Key, string Tail) label,
        bool selected,
        int width
    )
    {
        string gutter = selected ? Marker : Gutter;
        int available = Math.Max(0, width - CellText.Width(gutter));
        if (CellText.Width(label.Tail) + 2 <= available)
        {
            return [new(gutter, ApprovalTone.Plain), .. ChoiceSpans(label)];
        }

        if (available >= 3)
        {
            // The tail starts with the key's closing bracket; keep that bracket, then as much of
            // the label as fits, so the row still says which decision it is.
            string tail = label.Tail.StartsWith(']') ? label.Tail[1..] : label.Tail;
            string rest = CellText.Clip(tail, available - 3);
            return
            [
                new(gutter, ApprovalTone.Plain),
                new(label.Open, ApprovalTone.Plain),
                new(label.Key, ApprovalTone.Accent),
                new("]", ApprovalTone.Plain),
                new(rest, ApprovalTone.Plain),
            ];
        }

        return
        [
            .. Clip(
                [new(gutter + label.Open + label.Key + label.Tail, ApprovalTone.Plain)],
                ApprovalPart.Question,
                width
            ).Spans,
        ];
    }

    /// <summary>
    /// The <c>?</c> legend: what each key does, in the panel's own gutter. Part <see cref="ApprovalPart.Legend"/>,
    /// so the trimmer may drop it before the question but after the detail, and it decides nothing.
    /// </summary>
    private static void AppendLegend(
        List<ApprovalPanelLine> lines,
        ApprovalPromptModel prompt,
        int width
    )
    {
        if (!prompt.ShowLegend)
        {
            return;
        }

        lines.Add(
            Clip(
                [
                    new ApprovalSpan(
                        Gutter + "y approve  n deny  a always for this session",
                        ApprovalTone.Muted
                    ),
                ],
                ApprovalPart.Legend,
                width
            )
        );
        lines.Add(
            Clip(
                [
                    new ApprovalSpan(
                        Gutter + "Space choose  Enter or Escape deny",
                        ApprovalTone.Muted
                    ),
                ],
                ApprovalPart.Legend,
                width
            )
        );
    }

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
