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

    /// <summary>The thinking window: the activity line plus the newest reasoning text lines.</summary>
    internal const int MaxThinkingLines = 12;

    internal const string Placeholder = "Ask anything, / for commands";

    /// <summary>The narrowest terminal that gets the input box's rules (design D9).</summary>
    internal const int RuleMinWidth = 80;

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
        // A notice is always one physical line: embedded newlines or controls would split the
        // frame, so sanitize before clipping; a notice that sanitizes to empty renders as none.
        string? notice = state.Notice is null ? null : CellText.SingleLine(state.Notice);
        var noticeLine = string.IsNullOrEmpty(notice) ? null : notice;
        var approvalLine = state.Approval is null
            ? null
            : ApprovalPromptRenderer.PlainText(state.Approval);
        IReadOnlyList<string> trustLines = state.Trust is null
            ? []
            : TrustPromptRenderer.PlainLines(state.Trust);
        IReadOnlyList<string> pickerLines = state.Picker is null
            ? []
            : SelectListRenderer.PlainLines(state.Picker);
        IReadOnlyList<string> autocompleteLines = AutocompleteRenderer.Lines(state, width);
        IReadOnlyList<string> thinkingLines = ThinkingLines(state, width);
        var footerLine =
            state.Loading is { Length: > 0 } loading ? loading
            : state.Footer is null ? null
            : StatusFooterRenderer.PlainText(state.Footer, width, state.FooterWidgets);
        int ruleLines = HasRules(width) ? 1 : 0;
        int preInputLines =
            thinkingLines.Count
            + (activityLine is null ? 0 : 1)
            + (subagentLine is null ? 0 : 1)
            + (noticeLine is null ? 0 : 1)
            + (approvalLine is null ? 0 : 1)
            + trustLines.Count
            + pickerLines.Count
            + autocompleteLines.Count
            + ruleLines;
        // The budget carries both rules and the footer, so the tail shares what is left of the
        // eight-line live area; the input box itself always gets its full eight lines.
        int fixedLines = preInputLines + ruleLines + (footerLine is null ? 0 : 1);
        var input = InputLine.Layout(state.Input, width, MaxLines);
        IReadOnlyList<string> inputLines =
            state.Input.Text.Length == 0 ? [PlaceholderLine(width, state.Color)] : input.Lines;
        var tail = TailLines(
            state.TailText,
            // The picker is modal: while it is open it owns the area above the input.
            state.Picker
                is null
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

        if (noticeLine is not null)
        {
            lines.Add(CellText.Clip(noticeLine, width));
        }

        if (approvalLine is not null)
        {
            lines.Add(CellText.Clip(approvalLine, width));
        }

        foreach (string trustLine in trustLines)
        {
            lines.Add(CellText.Clip(trustLine, width));
        }

        foreach (string pickerLine in pickerLines)
        {
            lines.Add(CellText.Clip(pickerLine, width));
        }

        // The autocomplete list sits between the transient state and the upper rule (design D1).
        lines.AddRange(autocompleteLines);

        if (ruleLines == 1)
        {
            lines.Add(RuleLine(width, state.Unicode));
        }

        lines.AddRange(inputLines);

        if (ruleLines == 1)
        {
            lines.Add(RuleLine(width, state.Unicode));
        }

        if (footerLine is not null)
        {
            lines.Add(CellText.Clip(footerLine, width));
        }

        int cursorRow = tail.Count + preInputLines + input.CursorRow;
        // A short terminal caps the frame: trim the transient prefix (tail first, then thinking
        // and the other state), never the box rules, input or footer. The cursor follows the
        // lines that remain above the input.
        int boxLines = inputLines.Count + (ruleLines * 2) + (footerLine is null ? 0 : 1);
        int transientLines = lines.Count - boxLines;
        if (state.Size.Rows > 0 && lines.Count > state.Size.Rows)
        {
            int trim = Math.Min(lines.Count - state.Size.Rows, transientLines);
            if (trim > 0)
            {
                lines.RemoveRange(0, trim);
                cursorRow -= trim;
            }
        }

        return new RenderedFrame(lines, cursorRow, input.CursorColumn);
    }

    private static bool HasRules(int width) => width >= RuleMinWidth;

    private static string RuleLine(int width, bool unicode) =>
        new(unicode ? UnicodeRule : AsciiRule, width);

    private static string PlaceholderLine(int width, ColorSystemSupport color)
    {
        string prompt = CellText.Clip("> ", width);
        string text = CellText.Clip(Placeholder, Math.Max(0, width - CellText.Width(prompt)));
        return prompt + LivePalette.Muted(text, color);
    }

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
