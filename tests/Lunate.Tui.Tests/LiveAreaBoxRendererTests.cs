using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// The input box's chrome: the upper rule at every width, the lower rule outside the compact
/// band, the empty-input placeholder in the prompt tone, the footer below the box and the cursor
/// math that follows.
/// </summary>
public sealed class LiveAreaBoxRendererTests
{
    private const string Placeholder = "Ask anything, / for commands";

    [Theory]
    [InlineData(40, 1)]
    [InlineData(50, 1)]
    [InlineData(60, 2)]
    [InlineData(80, 2)]
    [InlineData(120, 2)]
    public void Every_width_band_shows_the_rule_above_the_input(int width, int ruleCount)
    {
        var frame = LiveAreaRenderer.Render(
            new LiveAreaState { Size = new ConsoleSize(width, 24) }
        );

        string rule = new('─', width);
        Assert.Equal(rule, frame.Lines[0]);
        Assert.Equal(ruleCount, frame.Lines.Count(line => line == rule));
        Assert.StartsWith("> ", frame.Lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_input_shows_the_placeholder()
    {
        var frame = LiveAreaRenderer.Render(new LiveAreaState { Size = new ConsoleSize(50, 24) });

        string rule = new('─', 50);
        Assert.Equal([rule, "> " + Placeholder], frame.Lines);
        Assert.Equal(1, frame.CursorRow);
        Assert.Equal(2, frame.CursorColumn);
    }

    [Theory]
    [InlineData(ColorSystemSupport.TrueColor, "\u001b[38;2;135;175;215m")]
    [InlineData(ColorSystemSupport.EightBit, "\u001b[38;5;110m")]
    [InlineData(ColorSystemSupport.Detect, "\u001b[34m")]
    [InlineData(ColorSystemSupport.NoColors, "")]
    public void Empty_input_renders_the_placeholder_in_the_prompt_tone(
        ColorSystemSupport color,
        string sgr
    )
    {
        var frame = LiveAreaRenderer.Render(
            new LiveAreaState { Color = color, Size = new ConsoleSize(50, 24) }
        );

        string line = "> " + Placeholder;
        string styled = sgr.Length == 0 ? line : sgr + line + "\u001b[0m";
        string rule = new('─', 50);
        Assert.Equal([rule, styled], frame.Lines);
    }

    [Fact]
    public void Typed_input_keeps_only_the_marker_in_the_prompt_tone()
    {
        var state = new LiveAreaState
        {
            Input = new InputLineState("go", 2),
            Color = ColorSystemSupport.TrueColor,
            Size = new ConsoleSize(50, 12),
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 50);
        Assert.Equal([rule, "\u001b[38;2;135;175;215m>\u001b[0m go"], frame.Lines);
    }

    [Fact]
    public void The_footer_renders_below_the_input()
    {
        var state = new LiveAreaState
        {
            Input = new InputLineState("go", 2),
            Footer = new StatusFooterModel("m", 10, 0, "/w", null),
            Size = new ConsoleSize(50, 12),
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 50);
        Assert.Equal([rule, "> go", "m"], frame.Lines);
        Assert.Equal(1, frame.CursorRow);
    }

    [Fact]
    public void The_box_rules_span_the_full_width_at_one_hundred_and_twenty()
    {
        var state = new LiveAreaState
        {
            Input = new InputLineState("go", 2),
            Footer = new StatusFooterModel("m", 10, 0, "/w", null),
            Size = new ConsoleSize(120, 12),
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 120);
        Assert.Equal([rule, "> go", rule, "m · 10/0 · /w"], frame.Lines);
        Assert.Equal(1, frame.CursorRow);
    }

    [Fact]
    public void The_box_rules_span_the_full_width_at_eighty_columns()
    {
        var state = new LiveAreaState
        {
            Input = new InputLineState("go", 2),
            Size = new ConsoleSize(80, 12),
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 80);
        Assert.Equal([rule, "> go", rule], frame.Lines);
    }

    [Fact]
    public void Empty_input_renders_the_placeholder_inside_the_box_at_eighty_columns()
    {
        var frame = LiveAreaRenderer.Render(new LiveAreaState { Size = new ConsoleSize(80, 12) });

        string rule = new('─', 80);
        Assert.Equal([rule, "> " + Placeholder, rule], frame.Lines);
        Assert.Equal(1, frame.CursorRow);
        Assert.Equal(2, frame.CursorColumn);
    }

    [Fact]
    public void The_box_draws_both_rules_at_seventy_nine_columns()
    {
        var state = new LiveAreaState
        {
            Input = new InputLineState("go", 2),
            Size = new ConsoleSize(79, 12),
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 79);
        Assert.Equal([rule, "> go", rule], frame.Lines);
    }

    [Fact]
    public void Only_the_upper_rule_renders_below_sixty_columns()
    {
        var state = new LiveAreaState
        {
            Input = new InputLineState("go", 2),
            Footer = new StatusFooterModel("m", 10, 0, "/w", null),
            Size = new ConsoleSize(50, 12),
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 50);
        Assert.Equal([rule, "> go", "m"], frame.Lines);
    }

    [Fact]
    public void Input_cursor_rows_account_for_the_upper_rule()
    {
        var state = new LiveAreaState
        {
            Input = new InputLineState("a\nb", 3),
            Size = new ConsoleSize(120, 12),
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 120);
        Assert.Equal([rule, "> a", "  b", rule], frame.Lines);
        Assert.Equal(2, frame.CursorRow);
        Assert.Equal(3, frame.CursorColumn);
    }

    [Fact]
    public void The_frame_stays_within_the_eight_line_budget_with_rules_and_footer()
    {
        string tail = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"line {i}"));
        var state = new LiveAreaState
        {
            TailText = tail,
            Footer = new StatusFooterModel("m", 10, 0, "/w", null),
            Size = new ConsoleSize(120, 12),
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 120);
        Assert.Equal(
            [
                "line 17",
                "line 18",
                "line 19",
                "line 20",
                rule,
                "> " + Placeholder,
                rule,
                "m · 10/0 · /w",
            ],
            frame.Lines
        );
        Assert.Equal(5, frame.CursorRow);
    }

    [Fact]
    public void The_rules_use_ascii_when_unicode_is_unavailable()
    {
        var state = new LiveAreaState
        {
            Input = new InputLineState("go", 2),
            Unicode = false,
            Size = new ConsoleSize(120, 12),
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('-', 120);
        Assert.Equal([rule, "> go", rule], frame.Lines);
        Assert.All(frame.Lines, line => Assert.DoesNotContain("─", line, StringComparison.Ordinal));
    }

    [Fact]
    public void The_input_keeps_its_lines_when_the_chrome_fills_the_budget()
    {
        string thinking = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"thought {i}"));
        var state = new LiveAreaState
        {
            Input = new InputLineState("a\nb\nc", 5),
            Thinking = new ThinkingState { Text = thinking },
            Footer = new StatusFooterModel("m", 10, 0, "/w", null),
            Size = new ConsoleSize(120, 24),
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 120);
        Assert.Equal(rule, frame.Lines[12]);
        Assert.Equal("> a", frame.Lines[13]);
        Assert.Equal("  b", frame.Lines[14]);
        Assert.Equal("  c", frame.Lines[15]);
        Assert.Equal(rule, frame.Lines[16]);
        Assert.Equal("m · 10/0 · /w", frame.Lines[17]);
        Assert.Equal(15, frame.CursorRow);
    }

    [Fact]
    public void The_frame_is_capped_at_the_terminal_rows()
    {
        string tail = string.Join('\n', Enumerable.Range(1, 4).Select(i => $"tail {i}"));
        string thinking = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"thought {i}"));
        var state = new LiveAreaState
        {
            TailText = tail,
            Thinking = new ThinkingState { Text = thinking },
            Footer = new StatusFooterModel("m", 10, 0, "/w", null),
            Size = new ConsoleSize(120, 10),
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 120);
        Assert.Equal(10, frame.Lines.Count);
        Assert.Equal("thought 15", frame.Lines[0]);
        Assert.Equal("thought 20", frame.Lines[5]);
        Assert.Equal(rule, frame.Lines[6]);
        Assert.Equal("> " + Placeholder, frame.Lines[7]);
        Assert.Equal(rule, frame.Lines[8]);
        Assert.Equal("m · 10/0 · /w", frame.Lines[9]);
        Assert.Equal(7, frame.CursorRow);
    }

    [Fact]
    public void The_input_renders_the_eight_lines_around_the_cursor()
    {
        string text = string.Join('\n', Enumerable.Range(1, 12).Select(i => $"l{i}"));
        var state = new LiveAreaState
        {
            Input = new InputLineState(text, text.Length),
            Footer = new StatusFooterModel("m", 10, 0, "/w", null),
            Size = new ConsoleSize(120, 24),
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 120);
        Assert.Equal(11, frame.Lines.Count);
        Assert.Equal(rule, frame.Lines[0]);
        Assert.Equal("  l5", frame.Lines[1]);
        Assert.Equal("  l12", frame.Lines[8]);
        Assert.Equal(rule, frame.Lines[9]);
        Assert.Equal("m · 10/0 · /w", frame.Lines[10]);
        Assert.Equal(8, frame.CursorRow);
        Assert.Equal(5, frame.CursorColumn);
    }
}
