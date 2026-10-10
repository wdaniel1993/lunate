using System.Text;

namespace Lunate.Tui;

public sealed record InputLineState(string Text, int CursorPosition);

public static class InputLine
{
    public static readonly InputLineState Empty = new(string.Empty, 0);

    internal const int PromptWidth = 2;

    public static InputLineState Apply(InputLineState state, KeyEvent key) =>
        key switch
        {
            { Kind: KeyKind.Character, Ctrl: false, Alt: false } => Insert(
                state,
                key.Text ?? string.Empty
            ),
            { Kind: KeyKind.Space, Ctrl: false, Alt: false } => Insert(state, " "),
            { Kind: KeyKind.Paste } => Insert(state, key.Text ?? string.Empty),
            { Kind: KeyKind.Backspace } => Backspace(state),
            { Kind: KeyKind.Delete } => Delete(state),
            { Kind: KeyKind.Left } => MoveLeft(state),
            { Kind: KeyKind.Right } => MoveRight(state),
            { Kind: KeyKind.Home } => MoveHome(state),
            { Kind: KeyKind.End } => MoveEnd(state),
            { Kind: KeyKind.Enter, Alt: true } => Insert(state, "\n"),
            { Kind: KeyKind.Enter, Shift: true } => Insert(state, "\n"),
            { Kind: KeyKind.Character, Ctrl: true, Text: "j" } => Insert(state, "\n"),
            _ => state,
        };

    public static InputLineState Insert(InputLineState state, string text)
    {
        if (text.Length == 0)
        {
            return state;
        }

        int cursor = ClampCursor(state);
        return new InputLineState(
            state.Text[..cursor] + text + state.Text[cursor..],
            cursor + text.Length
        );
    }

    public static InputLineState Backspace(InputLineState state)
    {
        int cursor = ClampCursor(state);
        if (cursor == 0)
        {
            return state;
        }

        int start = PreviousBoundary(state.Text, cursor);
        return new InputLineState(state.Text[..start] + state.Text[cursor..], start);
    }

    public static InputLineState Delete(InputLineState state)
    {
        int cursor = ClampCursor(state);
        if (cursor >= state.Text.Length)
        {
            return state;
        }

        int end = NextBoundary(state.Text, cursor);
        return new InputLineState(state.Text[..cursor] + state.Text[end..], cursor);
    }

    public static InputLineState MoveLeft(InputLineState state)
    {
        int cursor = ClampCursor(state);
        return cursor == 0
            ? state
            : state with
            {
                CursorPosition = PreviousBoundary(state.Text, cursor),
            };
    }

    public static InputLineState MoveRight(InputLineState state)
    {
        int cursor = ClampCursor(state);
        return cursor >= state.Text.Length
            ? state
            : state with
            {
                CursorPosition = NextBoundary(state.Text, cursor),
            };
    }

    public static InputLineState MoveHome(InputLineState state) =>
        ClampCursor(state) == 0 ? state : state with { CursorPosition = 0 };

    public static InputLineState MoveEnd(InputLineState state) =>
        ClampCursor(state) >= state.Text.Length
            ? state
            : state with
            {
                CursorPosition = state.Text.Length,
            };

    internal static InputLineLayout Layout(InputLineState state, int width, int maxLines)
    {
        int contentWidth = Math.Max(1, width - PromptWidth);
        var lines = new List<string>();
        var builder = new StringBuilder();
        var column = 0;
        var cursorRow = 0;
        var cursorColumn = PromptWidth;
        int cursor = ClampCursor(state);
        var index = 0;
        var cursorPlaced = false;

        foreach (var rune in state.Text.EnumerateRunes())
        {
            if (rune.Value == '\n')
            {
                if (index == cursor)
                {
                    cursorRow = lines.Count;
                    cursorColumn = PromptWidth + column;
                    cursorPlaced = true;
                }

                lines.Add(builder.ToString());
                builder.Clear();
                column = 0;
                index++;
                continue;
            }

            int runeWidth = CellText.Width(rune);
            if (column + runeWidth > contentWidth && column > 0)
            {
                lines.Add(builder.ToString());
                builder.Clear();
                column = 0;
            }

            if (index == cursor)
            {
                cursorRow = lines.Count;
                cursorColumn = PromptWidth + column;
                cursorPlaced = true;
            }

            builder.Append(rune);
            column += runeWidth;
            index += rune.Utf16SequenceLength;
        }

        lines.Add(builder.ToString());
        if (!cursorPlaced)
        {
            cursorRow = lines.Count - 1;
            cursorColumn = PromptWidth + column;
        }

        var start = 0;
        if (maxLines > 0 && lines.Count > maxLines)
        {
            start = Math.Clamp(cursorRow - maxLines + 1, 0, lines.Count - maxLines);
            lines = lines.GetRange(start, maxLines);
            cursorRow -= start;
        }

        var display = new List<string>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            string prefix = start + i == 0 ? "> " : "  ";
            display.Add(CellText.Clip(prefix + lines[i], width));
        }

        return new InputLineLayout(display, cursorRow, Math.Min(cursorColumn, width));
    }

    private static int ClampCursor(InputLineState state) =>
        Math.Clamp(state.CursorPosition, 0, state.Text.Length);

    private static int PreviousBoundary(string text, int cursor) =>
        cursor >= 2
        && char.IsLowSurrogate(text[cursor - 1])
        && char.IsHighSurrogate(text[cursor - 2])
            ? cursor - 2
            : cursor - 1;

    private static int NextBoundary(string text, int cursor) =>
        cursor + 1 < text.Length
        && char.IsHighSurrogate(text[cursor])
        && char.IsLowSurrogate(text[cursor + 1])
            ? cursor + 2
            : cursor + 1;
}

internal readonly record struct InputLineLayout(
    IReadOnlyList<string> Lines,
    int CursorRow,
    int CursorColumn
);
