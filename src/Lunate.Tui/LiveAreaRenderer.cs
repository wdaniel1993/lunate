namespace Lunate.Tui;

internal readonly record struct RenderedFrame(
    IReadOnlyList<string> Lines,
    int CursorRow,
    int CursorColumn
);

internal static class LiveAreaRenderer
{
    internal const int MaxLines = 8;

    private static readonly char[] SpinnerFrames = ['|', '/', '-', '\\'];

    public static RenderedFrame Render(LiveAreaState state)
    {
        int width = Math.Max(1, state.Size.Columns);
        var toolLine = ToolLine(state);
        var footerLine = FooterLine(state);
        int fixedLines = (toolLine is null ? 0 : 1) + (footerLine is null ? 0 : 1);
        var input = InputLine.Layout(state.Input, width, Math.Max(1, MaxLines - fixedLines));
        var tail = TailLines(
            state.TailText,
            Math.Max(0, MaxLines - fixedLines - input.Lines.Count),
            width
        );

        var lines = new List<string>();
        lines.AddRange(tail);
        if (toolLine is not null)
        {
            lines.Add(CellText.Clip(toolLine, width));
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

    private static string? FooterLine(LiveAreaState state)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(state.Model))
        {
            parts.Add(state.Model);
        }

        long tokens = state.InputTokens + state.OutputTokens;
        if (tokens > 0)
        {
            parts.Add(FormattableString.Invariant($"{tokens} tok"));
        }

        if (state.ContextPercent > 0)
        {
            parts.Add(FormattableString.Invariant($"{state.ContextPercent:0.#}% ctx"));
        }

        if (!string.IsNullOrEmpty(state.WorkingDirectory))
        {
            parts.Add(state.WorkingDirectory);
        }

        if (!string.IsNullOrEmpty(state.GitBranch))
        {
            parts.Add(state.GitBranch);
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
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
