namespace Lunate.Tui;

internal readonly record struct RenderedFrame(
    IReadOnlyList<string> Lines,
    int CursorRow,
    int CursorColumn
);

internal static class LiveAreaRenderer
{
    internal const int MaxLines = 8;

    // ASCII by design: the spinner is the guide's no-Unicode fallback. A Unicode spinner
    // variant must consult TerminalCapabilities (T-33) before it lands.
    private static readonly char[] SpinnerFrames = ['|', '/', '-', '\\'];

    public static RenderedFrame Render(LiveAreaState state)
    {
        int width = Math.Max(1, state.Size.Columns);
        var toolLine = ToolLine(state);
        var noticeLine = string.IsNullOrEmpty(state.Notice) ? null : state.Notice;
        var approvalLine = state.Approval is null
            ? null
            : ApprovalPromptRenderer.PlainText(state.Approval);
        IReadOnlyList<string> pickerLines = state.Picker is null
            ? []
            : SelectListRenderer.PlainLines(state.Picker);
        var footerLine = state.Footer is null
            ? null
            : StatusFooterRenderer.PlainText(state.Footer, width);
        int fixedLines =
            (toolLine is null ? 0 : 1)
            + (noticeLine is null ? 0 : 1)
            + (approvalLine is null ? 0 : 1)
            + pickerLines.Count
            + (footerLine is null ? 0 : 1);
        var input = InputLine.Layout(state.Input, width, Math.Max(1, MaxLines - fixedLines));
        var tail = TailLines(
            state.TailText,
            // The picker is modal: while it is open it owns the area above the input.
            state.Picker
                is null
                ? Math.Max(0, MaxLines - fixedLines - input.Lines.Count)
                : 0,
            width
        );

        var lines = new List<string>();
        lines.AddRange(tail);
        if (toolLine is not null)
        {
            lines.Add(CellText.Clip(toolLine, width));
        }

        if (noticeLine is not null)
        {
            lines.Add(CellText.Clip(noticeLine, width));
        }

        if (approvalLine is not null)
        {
            lines.Add(CellText.Clip(approvalLine, width));
        }

        foreach (string pickerLine in pickerLines)
        {
            lines.Add(CellText.Clip(pickerLine, width));
        }

        if (footerLine is not null)
        {
            lines.Add(CellText.Clip(footerLine, width));
        }

        lines.AddRange(input.Lines);

        return new RenderedFrame(
            lines,
            tail.Count + fixedLines + input.CursorRow,
            input.CursorColumn
        );
    }

    private static string? ToolLine(LiveAreaState state) =>
        state.ToolName is { Length: > 0 } name
            ? $"{SpinnerFrames[state.FrameNumber % SpinnerFrames.Length]} {name}"
            : null;

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
