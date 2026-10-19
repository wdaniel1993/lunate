using Spectre.Console;

namespace Lunate.Tui;

/// <summary>
/// The autocomplete list's live rows (design D4/D9): at most eight rows directly above the input
/// box's upper rule. The gutter carries the selection as <c>&gt; </c> when colour cannot show it
/// (NO_COLOR) or the terminal has no Unicode; names use the accent tone, descriptions and file
/// directories the muted tone. Rows are composed and clipped in plain text, then styled, so SGR
/// bytes never count as columns and nothing wraps.
/// </summary>
internal static class AutocompleteRenderer
{
    /// <summary>The narrowest terminal that shows row descriptions (design D9).</summary>
    internal const int DescriptionMinWidth = 60;

    private const string Gutter = "  ";
    private const string SelectedGutter = "> ";

    internal static IReadOnlyList<string> Lines(LiveAreaState state, int width)
    {
        if (state.Autocomplete is not { Items.Count: > 0 } autocomplete)
        {
            return [];
        }

        int selected = Math.Clamp(autocomplete.SelectedIndex, 0, autocomplete.Items.Count - 1);
        int count = Math.Min(autocomplete.Items.Count, AutocompleteState.MaxRows);
        var lines = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            lines.Add(Row(autocomplete.Items[index], index == selected, state, width));
        }

        return lines;
    }

    private static string Row(CompletionItem item, bool selected, LiveAreaState state, int width)
    {
        string gutter = CellText.Clip(
            selected && (state.Color == ColorSystemSupport.NoColors || !state.Unicode)
                ? SelectedGutter
                : Gutter,
            width
        );
        int room = Math.Max(0, width - CellText.Width(gutter));
        string row =
            item.Kind == CompletionKind.File
                ? FileRow(item, selected, state.Color, room, width)
                : TextRow(item, selected, state.Color, room, width);
        return gutter + row;
    }

    private static string TextRow(
        CompletionItem item,
        bool selected,
        ColorSystemSupport color,
        int room,
        int width
    )
    {
        string name = CellText.Clip(item.Text, room);
        string row = Tone(name, selected, color);
        if (width < DescriptionMinWidth || string.IsNullOrEmpty(item.Description))
        {
            return row;
        }

        int remaining = room - CellText.Width(name);
        // A clipped name means the row is full; never squeeze a description past the edge.
        if (CellText.Width(name) < CellText.Width(item.Text) || remaining <= 2)
        {
            return row;
        }

        string description = CellText.Clip(item.Description, remaining - 2);
        return description.Length == 0 ? row : row + "  " + LivePalette.Muted(description, color);
    }

    private static string FileRow(
        CompletionItem item,
        bool selected,
        ColorSystemSupport color,
        int room,
        int width
    )
    {
        string directory = item.Muted ?? string.Empty;
        string name = item.Text[directory.Length..];
        // Below the description breakpoint a path that does not fit keeps only its basename.
        if (width < DescriptionMinWidth && CellText.Width(item.Text) > room)
        {
            directory = string.Empty;
        }

        string shownDirectory = CellText.Clip(directory, room);
        string shownName = CellText.Clip(name, Math.Max(0, room - CellText.Width(shownDirectory)));
        return LivePalette.Muted(shownDirectory, color) + Tone(shownName, selected, color);
    }

    private static string Tone(string text, bool selected, ColorSystemSupport color) =>
        selected ? LivePalette.Accent(text, color) : text;
}
