using System.Text;
using Spectre.Console;

namespace Lunate.Tui;

internal readonly record struct RenderedFrame(
    IReadOnlyList<string> Lines,
    int CursorRow,
    int CursorColumn
);

internal static class LiveAreaRenderer
{
    internal const int MaxLines = 8;

    /// <summary>The most rows a wrapped notice may occupy before it is elided.</summary>
    internal const int MaxNoticeLines = 4;

    /// <summary>The thinking window: the activity line plus the newest reasoning text lines.</summary>
    internal const int MaxThinkingLines = 12;

    internal const string Placeholder = "Ask anything, / for commands";

    private const char UnicodeRule = '─';
    private const char AsciiRule = '-';

    // ASCII by design: the spinner is the guide's no-Unicode fallback. A Unicode spinner
    // variant must consult TerminalCapabilities (T-33) before it lands.
    private static readonly char[] SpinnerFrames = ['|', '/', '-', '\\'];

    public static RenderedFrame Render(LiveAreaState state)
    {
        int width = Math.Max(1, state.Size.Columns);
        var activityLine = ActivityLine(state, width);
        var subagentLine = state.SubagentRunning ? "subagent running" : null;
        // Embedded newlines or controls would split the frame, so sanitize before wrapping; a
        // notice that sanitizes to empty renders as none, and a long one wraps instead of losing
        // its tail (the part that says what to do).
        string? notice = state.Notice is null ? null : CellText.SingleLine(state.Notice);
        IReadOnlyList<string> noticeLines = string.IsNullOrEmpty(notice)
            ? []
            : NoticeLines(notice, width, state.Unicode);
        // The live state carries only the mask: the typed characters never reach a frame.
        string? secretPromptLine = state.SecretPrompt is { } secretPrompt
            ? CellText.Clip(secretPrompt.Label + ": " + secretPrompt.Mask, width)
            : null;
        IReadOnlyList<ApprovalPanelLine> approvalLines = state.Approval is null
            ? []
            : ApprovalPromptRenderer.PlainLines(state.Approval, width, state.Color, state.Unicode);
        IReadOnlyList<string> trustLines = state.Trust is null
            ? []
            : TrustPromptRenderer.PlainLines(state.Trust, width, state.Unicode);
        IReadOnlyList<string> pickerLines = state.Picker is null
            ? []
            : SelectListRenderer.PlainLines(state.Picker);
        // The input box, the two rules and the footer take roughly six lines; the overlay gets the
        // rest, and its renderer keeps the title and the hint inside that budget.
        IReadOnlyList<string> keyHelpLines = state.KeyHelp is null
            ? []
            : KeyHelpRenderer.PlainLines(state.KeyHelp, Math.Max(6, state.Size.Rows - 8));
        IReadOnlyList<string> autocompleteLines = AutocompleteRenderer.Lines(state, width);
        IReadOnlyList<string> thinkingLines = ThinkingLines(state, width);
        var footerLine =
            state.Loading is { Length: > 0 } loading ? loading
            : state.Footer is null ? null
            : StatusFooterRenderer.PlainText(
                state.Footer,
                width,
                state.FooterWidgets,
                state.QueuedCount,
                state.QueuedFirst
            );
        // The box always closes: the lower rule is part of the fixed chrome at every width.
        const int lowerRuleLines = 1;
        int preInputLines =
            thinkingLines.Count
            + (activityLine is null ? 0 : 1)
            + (subagentLine is null ? 0 : 1)
            + noticeLines.Count
            + (secretPromptLine is null ? 0 : 1)
            + approvalLines.Count
            + trustLines.Count
            + pickerLines.Count
            + keyHelpLines.Count
            + autocompleteLines.Count
            // The upper rule is always drawn, so it belongs to the fixed prefix.
            + 1;
        // The budget carries the rules and the footer, so the tail shares what is left of the
        // eight-line live area; the input box itself always gets its full eight lines.
        int fixedLines = preInputLines + lowerRuleLines + (footerLine is null ? 0 : 1);
        var input = InputLine.Layout(state.Input, width, MaxLines, state.BashMode);
        IReadOnlyList<string> inputLines =
            state.Input.Text.Length == 0
                ?
                [
                    state.BashMode
                        ? BashPromptLine(width, state.Color)
                        : PlaceholderLine(width, state.Color),
                ]
                : PromptLines(input.Lines, state.Color, state.BashMode);
        var tail = TailLines(
            state.TailText,
            // The picker is modal: while it is open it owns the area above the input.
            state.Picker
                is null
            && state.KeyHelp is null
                ? Math.Max(0, MaxLines - fixedLines - inputLines.Count)
                : 0,
            width
        );

        var lines = new List<string>();
        lines.AddRange(tail);
        if (activityLine is not null)
        {
            lines.Add(activityLine);
        }

        lines.AddRange(thinkingLines);
        if (subagentLine is not null)
        {
            lines.Add(CellText.Clip(subagentLine, width));
        }

        foreach (string noticeLine in noticeLines)
        {
            lines.Add(noticeLine);
        }

        if (secretPromptLine is not null)
        {
            lines.Add(secretPromptLine);
        }

        int approvalStart = lines.Count;
        foreach (var approvalLine in approvalLines)
        {
            lines.Add(approvalLine.Text);
        }

        foreach (string trustLine in trustLines)
        {
            lines.Add(CellText.Clip(trustLine, width));
        }

        foreach (string pickerLine in pickerLines)
        {
            lines.Add(CellText.Clip(pickerLine, width));
        }

        foreach (string keyHelpLine in keyHelpLines)
        {
            lines.Add(CellText.Clip(keyHelpLine, width));
        }

        // The autocomplete list sits between the transient state and the upper rule (design D1).
        lines.AddRange(autocompleteLines);

        lines.Add(RuleLine(width, state.Unicode));
        lines.AddRange(inputLines);

        if (lowerRuleLines == 1)
        {
            lines.Add(RuleLine(width, state.Unicode));
        }

        if (footerLine is not null)
        {
            // The status line takes the theme's tone: muted in the colour modes, plain under
            // mono. Width math happens before styling, so the SGR bytes must not count as columns.
            lines.Add(LivePalette.Muted(CellText.Clip(footerLine, width), state.Color));
        }

        int cursorRow = tail.Count + preInputLines + input.CursorRow;
        // A short terminal caps the frame: trim the transient prefix (tail first, then thinking
        // and the other state), then an approval panel's detail before its headline, never the
        // box rules, input, footer or question. The cursor follows the lines above the input.
        int boxLines = inputLines.Count + 1 + lowerRuleLines + (footerLine is null ? 0 : 1);
        int transientLines = lines.Count - boxLines;
        LiveAreaTrim.Apply(
            lines,
            state.Size.Rows,
            transientLines,
            approvalStart,
            approvalLines,
            ref cursorRow
        );

        return new RenderedFrame(lines, cursorRow, input.CursorColumn);
    }

    private static string RuleLine(int width, bool unicode) =>
        new(unicode ? UnicodeRule : AsciiRule, width);

    private static string PlaceholderLine(int width, ColorSystemSupport color)
    {
        string prompt = CellText.Clip("> ", width);
        string text = CellText.Clip(Placeholder, Math.Max(0, width - CellText.Width(prompt)));
        // The whole line carries the prompt tone, so an empty input still reads as something
        // to type into rather than as a line of output.
        return LivePalette.Prompt(prompt + text, color);
    }

    /// <summary>
    /// Paints the prompt marker of the first visible input line in its tone; the typed text and
    /// the continuation indent stay plain, matching the committed user echo. Bash mode paints
    /// the whole <c>! </c> marker in the accent tone.
    /// </summary>
    private static IReadOnlyList<string> PromptLines(
        IReadOnlyList<string> lines,
        ColorSystemSupport color,
        bool bashMode
    )
    {
        if (color == ColorSystemSupport.NoColors)
        {
            return lines;
        }

        int markerWidth = bashMode ? 2 : 1;
        var styled = new List<string>(lines.Count);
        foreach (string line in lines)
        {
            if (line.Length < markerWidth || !line.StartsWith('>') && !line.StartsWith('!'))
            {
                styled.Add(line);
                continue;
            }

            string marker = line[..markerWidth];
            string tone = bashMode
                ? LivePalette.Accent(marker, color)
                : LivePalette.Prompt(marker, color);
            styled.Add(tone + line[markerWidth..]);
        }

        return styled;
    }

    /// <summary>The empty bash-mode prompt: the accent-toned <c>! </c> marker and nothing else.</summary>
    private static string BashPromptLine(int width, ColorSystemSupport color) =>
        LivePalette.Accent(CellText.Clip("! ", width), color);

    private static string ThinkingLabel(int elapsedSeconds) =>
        FormattableString.Invariant($"⠹ thinking… {elapsedSeconds}s");

    private static string ToolLabel(LiveAreaState state, string name, int width)
    {
        string text = FormattableString.Invariant(
            $"{SpinnerFrames[state.FrameNumber % SpinnerFrames.Length]} {CellText.SingleLine(name)}"
        );
        string tail = FormattableString.Invariant($" · {state.ToolElapsedSeconds}s");
        string metrics = MetricsSuffix(state, width);
        string summary = CellText.SingleLine(state.ToolSummary ?? string.Empty);
        if (summary.Length > 0)
        {
            // Budget the summary, never the tail: a long argument blob must not clip the
            // elapsed seconds, the deltas or the interrupt hint off the line.
            int budget =
                width - CellText.Width(text) - CellText.Width(tail) - CellText.Width(metrics) - 1;
            if (budget > 0)
            {
                text += " " + CellText.Clip(summary, budget);
            }
        }

        return text + tail + metrics;
    }

    private static string WorkingLabel(LiveAreaState state) =>
        FormattableString.Invariant(
            $"{SpinnerFrames[state.FrameNumber % SpinnerFrames.Length]} working… {state.RunElapsedSeconds}s"
        );

    /// <summary>
    /// The one activity line: the thinking label, the active tool with its summary, or the generic
    /// working label, followed by the run's deltas and the interrupt hint while cancellable. The
    /// thinking label carries the thinking colour; every other part stays plain.
    /// </summary>
    private static string? ActivityLine(LiveAreaState state, int width)
    {
        string? text =
            state.Thinking is { } thinking
                ? ThinkingLabel(thinking.ElapsedSeconds) + MetricsSuffix(state, width)
            : state.ToolName is { Length: > 0 } name ? ToolLabel(state, name, width)
            : state.Running ? WorkingLabel(state) + MetricsSuffix(state, width)
            : null;
        if (text is null)
        {
            return null;
        }

        string line = CellText.Clip(text, width);
        if (
            state.Thinking is { } current
            && state.Color != ColorSystemSupport.NoColors
            && line.StartsWith(
                CellText.Clip(ThinkingLabel(current.ElapsedSeconds), width),
                StringComparison.Ordinal
            )
        )
        {
            string label = CellText.Clip(ThinkingLabel(current.ElapsedSeconds), width);
            return ThinkingColor.Wrap(label, state.Color) + line[label.Length..];
        }

        return line;
    }

    /// <summary>
    /// The run's token deltas and interrupt hint: only on the wide bands and only while the turn
    /// is cancellable. The deltas are omitted until both usage counts are known.
    /// </summary>
    private static string MetricsSuffix(LiveAreaState state, int width)
    {
        if (!state.Cancellable || WidthBands.IsCompact(width))
        {
            return string.Empty;
        }

        string text = string.Empty;
        if (state.RunInputTokens is { } input && state.RunOutputTokens is { } output)
        {
            text =
                " · ↑"
                + StatusFooterRenderer.FormatTokens(input)
                + " ↓"
                + StatusFooterRenderer.FormatTokens(output);
        }

        return text + " · esc to interrupt";
    }

    /// <summary>
    /// The live thinking body: the newest wrapped reasoning text, in the thinking colour. The
    /// window is at most <see cref="MaxThinkingLines"/> lines counting the activity line above it,
    /// so older text scrolls away; the colour is applied after wrapping and clipping, so SGR bytes
    /// never count as width.
    /// </summary>
    private static IReadOnlyList<string> ThinkingLines(LiveAreaState state, int width)
    {
        if (state.Thinking is not { Text.Length: > 0 } thinking)
        {
            return [];
        }

        IReadOnlyList<string> text = WrappedLines(thinking.Text, width);
        int start = Math.Max(0, text.Count - (MaxThinkingLines - 1));
        return
        [
            .. text.Skip(start)
                .Select(line => ThinkingColor.Wrap(CellText.Clip(line, width), state.Color)),
        ];
    }

    /// <summary>
    /// Wraps a sanitized notice to the width and caps it at <see cref="MaxNoticeLines"/>: an
    /// overflowing notice ends its last line with an ellipsis, so the input is never pushed out.
    /// </summary>
    private static IReadOnlyList<string> NoticeLines(string notice, int width, bool unicode)
    {
        List<string> lines =
        [
            .. WrapWords(notice, width).Select(line => CellText.Clip(line, width)),
        ];
        if (lines.Count <= MaxNoticeLines)
        {
            return lines;
        }

        List<string> capped = lines.Take(MaxNoticeLines).ToList();
        string marker = CellText.Clip(unicode ? "\u2026" : "...", width);
        string last = CellText.Clip(capped[^1], width - CellText.Width(marker)).TrimEnd();
        capped[^1] = last + marker;
        return capped;
    }

    /// <summary>Greedy word wrap at the width, breaking a single word wider than the width.</summary>
    private static List<string> WrapWords(string text, int width)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        var used = 0;
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int wordWidth = CellText.Width(word);
            if (current.Length > 0)
            {
                if (used + 1 + wordWidth <= width)
                {
                    current.Append(' ').Append(word);
                    used += 1 + wordWidth;
                    continue;
                }

                lines.Add(current.ToString());
                current.Clear();
                used = 0;
            }

            if (wordWidth <= width)
            {
                current.Append(word);
                used = wordWidth;
                continue;
            }

            foreach (var rune in word.EnumerateRunes())
            {
                int runeWidth = CellText.Width(rune);
                if (used > 0 && used + runeWidth > width)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                    used = 0;
                }

                current.Append(rune);
                used += runeWidth;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    /// <summary>Wraps every hard line at the width, breaking a run longer than the width.</summary>
    private static IReadOnlyList<string> WrappedLines(string text, int width)
    {
        var lines = new List<string>();
        foreach (string logical in text.Split('\n'))
        {
            var builder = new StringBuilder();
            var used = 0;
            foreach (var rune in logical.EnumerateRunes())
            {
                int runeWidth = CellText.Width(rune);
                if (used > 0 && used + runeWidth > width)
                {
                    lines.Add(builder.ToString());
                    builder.Clear();
                    used = 0;
                }

                builder.Append(rune);
                used += runeWidth;
            }

            lines.Add(builder.ToString());
        }

        return lines;
    }

    private static IReadOnlyList<string> TailLines(string tailText, int budget, int width)
    {
        if (budget <= 0 || tailText.Length == 0)
        {
            return [];
        }

        string[] all = tailText.Split('\n');
        int start = Math.Max(0, all.Length - budget);
        var lines = new List<string>(all.Length - start);
        for (var i = start; i < all.Length; i++)
        {
            lines.Add(CellText.Clip(all[i], width));
        }

        return lines;
    }
}
