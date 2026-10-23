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

    /// <summary>A kill: the resulting line and the text removed, for the kill ring.</summary>
    public readonly record struct KillResult(InputLineState State, string Killed);

    /// <summary>Moves the cursor to the previous word start; separators before the word are skipped.</summary>
    public static InputLineState MoveWordLeft(InputLineState state)
    {
        int cursor = ClampCursor(state);
        int index = cursor;
        while (index > 0 && !IsWordCharAt(state.Text, PreviousBoundary(state.Text, index)))
        {
            index = PreviousBoundary(state.Text, index);
        }

        while (index > 0 && IsWordCharAt(state.Text, PreviousBoundary(state.Text, index)))
        {
            index = PreviousBoundary(state.Text, index);
        }

        return index == cursor ? state : state with { CursorPosition = index };
    }

    /// <summary>Moves the cursor past the current word and the separators that follow it.</summary>
    public static InputLineState MoveWordRight(InputLineState state)
    {
        int cursor = ClampCursor(state);
        int index = cursor;
        while (index < state.Text.Length && !IsWordCharAt(state.Text, index))
        {
            index = NextBoundary(state.Text, index);
        }

        while (index < state.Text.Length && IsWordCharAt(state.Text, index))
        {
            index = NextBoundary(state.Text, index);
        }

        return index == cursor ? state : state with { CursorPosition = index };
    }

    /// <summary>Kills from the cursor to the end of the line.</summary>
    public static KillResult KillToEnd(InputLineState state)
    {
        int cursor = ClampCursor(state);
        string killed = state.Text[cursor..];
        return new KillResult(state with { Text = state.Text[..cursor] }, killed);
    }

    /// <summary>Kills from the start of the line to the cursor.</summary>
    public static KillResult KillToStart(InputLineState state)
    {
        int cursor = ClampCursor(state);
        string killed = state.Text[..cursor];
        return new KillResult(
            state with
            {
                Text = state.Text[cursor..],
                CursorPosition = 0,
            },
            killed
        );
    }

    /// <summary>Kills the word before the cursor, including the separators between them.</summary>
    public static KillResult KillWordBack(InputLineState state)
    {
        int cursor = ClampCursor(state);
        int start = MoveWordLeft(state).CursorPosition;
        string killed = state.Text[start..cursor];
        return new KillResult(
            state with
            {
                Text = state.Text[..start] + state.Text[cursor..],
                CursorPosition = start,
            },
            killed
        );
    }

    /// <summary>Kills from the cursor to the end of the next word.</summary>
    public static KillResult KillWordForward(InputLineState state)
    {
        int cursor = ClampCursor(state);
        int end = MoveWordRight(state).CursorPosition;
        string killed = state.Text[cursor..end];
        return new KillResult(
            state with
            {
                Text = state.Text[..cursor] + state.Text[end..],
            },
            killed
        );
    }

    private static bool IsWordCharAt(string text, int index)
    {
        if (index < 0 || index >= text.Length)
        {
            return false;
        }

        char character = text[index];
        return !char.IsSurrogate(character)
            && (char.IsLetterOrDigit(character) || character == '_');
    }

    internal static InputLineLayout Layout(InputLineState state, int width, int maxLines) =>
        Layout(state, width, maxLines, bashMode: false);

    internal static InputLineLayout Layout(
        InputLineState state,
        int width,
        int maxLines,
        bool bashMode
    )
    {
        string prompt = bashMode ? "! " : "> ";
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
            string prefix = start + i == 0 ? prompt : "  ";
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
