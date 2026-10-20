using System.Text.RegularExpressions;
using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// The autocomplete list's live rows (design D4/D9): at most eight rows directly above the upper
/// rule, the selection shown by colour or by the ASCII <c>&gt; </c> gutter, descriptions and file
/// directories muted, names accented, and every row clipped in plain text before styling.
/// </summary>
public sealed class AutocompleteRendererTests
{
    private const string Placeholder = "Ask anything, / for commands";

    [Fact]
    public void Rows_render_below_the_transient_state_and_above_the_upper_rule()
    {
        var state = State([Command("/help"), Command("/model")], selected: 1) with
        {
            ToolName = "read",
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 120);
        Assert.Equal(
            ["| read · 0s", "  /help", "> /model", rule, "> " + Placeholder, rule],
            frame.Lines
        );
        Assert.Equal(4, frame.CursorRow);
    }

    [Fact]
    public void At_most_eight_rows_render()
    {
        CompletionItem[] items = [.. Enumerable.Range(1, 9).Select(i => Command($"/c{i}"))];

        IReadOnlyList<string> rows = Rows(State(items));

        Assert.Equal(8, rows.Count);
        Assert.Equal("> /c1", rows[0]);
        Assert.Equal("  /c8", rows[7]);
    }

    [Fact]
    public void Descriptions_append_in_the_muted_tone_at_sixty_columns()
    {
        var state = State(
            [Command("/model", "Switch model"), Command("/help", "Show commands")],
            selected: 0,
            width: 60,
            color: ColorSystemSupport.TrueColor
        );

        IReadOnlyList<string> rows = Rows(state, 60);

        Assert.Equal("  /help  \u001b[38;2;138;138;138mShow commands\u001b[0m", rows[1]);
    }

    [Fact]
    public void At_eighty_columns_the_list_keeps_descriptions_inside_the_box()
    {
        var state = State(
            [Command("/help", "Show commands")],
            color: ColorSystemSupport.TrueColor,
            width: 80
        );

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 80);
        Assert.Equal(
            [
                "  \u001b[38;2;95;215;215m/help\u001b[0m  "
                    + "\u001b[38;2;138;138;138mShow commands\u001b[0m",
                rule,
                "\u001b[38;2;135;175;215m> " + Placeholder + "\u001b[0m",
                rule,
            ],
            frame.Lines
        );
        Assert.Equal(2, frame.CursorRow);
    }

    [Fact]
    public void Descriptions_drop_at_fifty_nine_columns()
    {
        var state = State(
            [Command("/model", "Switch model"), Command("/help", "Show commands")],
            width: 59
        );

        Assert.Equal(["> /model", "  /help"], Rows(state, 59));
    }

    [Fact]
    public void Descriptions_drop_below_sixty_columns()
    {
        var state = State(
            [Command("/model", "Switch model"), Command("/help", "Show commands")],
            selected: 0,
            width: 50
        );

        Assert.Equal(["> /model", "  /help"], Rows(state, 50));
    }

    [Fact]
    public void File_rows_mute_the_directory_and_leave_the_basename_plain()
    {
        var state = State(
            [File("src/core/File.cs"), File("src/core/Other.cs")],
            selected: 0,
            color: ColorSystemSupport.TrueColor
        );

        IReadOnlyList<string> rows = Rows(state);

        Assert.Equal("  \u001b[38;2;138;138;138msrc/core/\u001b[0mOther.cs", rows[1]);
    }

    [Fact]
    public void A_selected_file_renders_its_basename_in_the_accent_tone()
    {
        var state = State([File("src/core/File.cs")], color: ColorSystemSupport.TrueColor);

        IReadOnlyList<string> rows = Rows(state);

        Assert.Equal(
            "  \u001b[38;2;138;138;138msrc/core/\u001b[0m\u001b[38;2;95;215;215mFile.cs\u001b[0m",
            Assert.Single(rows)
        );
    }

    [Theory]
    [InlineData(ColorSystemSupport.TrueColor, "\u001b[38;2;95;215;215m")]
    [InlineData(ColorSystemSupport.EightBit, "\u001b[38;5;80m")]
    [InlineData(ColorSystemSupport.Detect, "\u001b[36m")]
    public void A_selected_command_renders_its_name_in_the_accent_tone(
        ColorSystemSupport color,
        string sgr
    )
    {
        IReadOnlyList<string> rows = Rows(State([Command("/help")], color: color));

        Assert.Equal("  " + sgr + "/help\u001b[0m", Assert.Single(rows));
    }

    [Fact]
    public void No_matches_render_no_rows()
    {
        var closed = new LiveAreaState { Size = new ConsoleSize(120, 24) };

        Assert.Empty(AutocompleteRenderer.Lines(closed, 120));
        Assert.Empty(Rows(State([])));
    }

    [Fact]
    public void Under_no_colors_the_selected_row_uses_the_ascii_marker()
    {
        var state = State([Command("/model"), Command("/help")], selected: 1);

        Assert.Equal(["  /model", "> /help"], Rows(state));
    }

    [Fact]
    public void A_selected_row_uses_the_marker_when_unicode_is_unavailable()
    {
        var state = State([Command("/help")], color: ColorSystemSupport.TrueColor, unicode: false);

        Assert.Equal(["> \u001b[38;2;95;215;215m/help\u001b[0m"], Rows(state));
    }

    [Fact]
    public void The_selected_index_is_clamped_to_the_last_row()
    {
        var state = State([Command("/model"), Command("/help")], selected: 99);

        Assert.Equal(["  /model", "> /help"], Rows(state));
    }

    [Fact]
    public void Rows_are_clipped_to_the_width_before_styling()
    {
        string name = "/" + new string('a', 60);
        var state = State([Command(name, "a description")], color: ColorSystemSupport.TrueColor);

        IReadOnlyList<string> rows = Rows(state, width: 40);

        string plain = StripSgr(Assert.Single(rows));
        Assert.Equal(40, CellText.Width(plain));
        Assert.Equal("  " + name[..38], plain);
    }

    [Fact]
    public void Rows_clip_even_the_gutter_at_tiny_widths()
    {
        IReadOnlyList<string> rows = Rows(State([Command("/help")]), width: 1);

        Assert.Equal([">"], rows);
    }

    [Fact]
    public void Long_file_paths_fall_back_to_the_basename_below_sixty_columns()
    {
        string path = new string('a', 40) + "/" + new string('b', 20) + "/File.cs";
        var state = State([File(path)], color: ColorSystemSupport.TrueColor);

        Assert.Equal(
            "  \u001b[38;2;95;215;215mFile.cs\u001b[0m",
            Assert.Single(Rows(state, width: 50))
        );
    }

    [Fact]
    public void Long_file_paths_clip_to_the_width_at_sixty_columns_and_above()
    {
        string path = new string('a', 40) + "/" + new string('b', 20) + "/File.cs";
        var state = State([File(path)], color: ColorSystemSupport.TrueColor);

        string plain = StripSgr(Assert.Single(Rows(state, width: 60)));

        Assert.Equal(60, CellText.Width(plain));
    }

    [Fact]
    public void The_list_rows_count_in_the_line_budget()
    {
        CompletionItem[] items = [.. Enumerable.Range(1, 8).Select(i => Command($"/c{i}"))];
        var state = State(items) with
        {
            TailText = "tail one\ntail two",
            Footer = new StatusFooterModel("m", 10, 0, "/w", null),
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 120);
        Assert.Equal(12, frame.Lines.Count);
        Assert.Equal("> /c1", frame.Lines[0]);
        Assert.Equal("  /c8", frame.Lines[7]);
        Assert.Equal(rule, frame.Lines[8]);
        Assert.Equal("> " + Placeholder, frame.Lines[9]);
        Assert.Equal(rule, frame.Lines[10]);
        Assert.Equal("m · 10/0 · /w", frame.Lines[11]);
        Assert.Equal(9, frame.CursorRow);
        Assert.DoesNotContain("tail one", frame.Lines);
    }

    private static LiveAreaState State(
        IReadOnlyList<CompletionItem> items,
        int selected = 0,
        int width = 120,
        ColorSystemSupport color = ColorSystemSupport.NoColors,
        bool unicode = true
    ) =>
        new()
        {
            Autocomplete = new AutocompleteState(
                items,
                selected,
                CompletionMode.Command,
                TokenStart: 0
            ),
            Color = color,
            Unicode = unicode,
            Size = new ConsoleSize(width, 24),
        };

    private static IReadOnlyList<string> Rows(LiveAreaState state, int width = 120) =>
        AutocompleteRenderer.Lines(state, width);

    private static CompletionItem Command(string text, string? description = null) =>
        new(text, description, CompletionKind.Command, null);

    private static CompletionItem File(string path) => CompletionItem.File(path);

    private static string StripSgr(string line) =>
        Regex.Replace(line, "\u001b\\[[0-9;]*m", string.Empty);
}
