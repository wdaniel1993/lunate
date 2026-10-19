using System.Text.RegularExpressions;
using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// The live thinking block's renderer contract: the prefix with the elapsed seconds, the newest
/// text lines within the twelve-line window, the thinking colour by capability (never under
/// NO_COLOR) and the block's place above footer and input.
/// </summary>
public sealed class LiveAreaThinkingRendererTests
{
    [Fact]
    public void Thinking_renders_the_prefix_with_the_elapsed_seconds_above_the_text()
    {
        var frame = LiveAreaRenderer.Render(WithThinking("reasoning text", elapsed: 3));

        Assert.Equal(
            ["⠹ thinking… 3s", "reasoning text", "> Ask anything, / for commands"],
            frame.Lines
        );
    }

    [Fact]
    public void Thinking_without_text_yet_renders_only_the_prefix()
    {
        var frame = LiveAreaRenderer.Render(WithThinking(string.Empty, elapsed: 1));

        Assert.Equal(["⠹ thinking… 1s", "> Ask anything, / for commands"], frame.Lines);
    }

    [Fact]
    public void Thinking_lines_render_above_the_box_and_its_footer()
    {
        var state = WithThinking("reasoning text", elapsed: 1) with
        {
            Footer = new StatusFooterModel("m", 10, 0, "/w", null),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal(
            ["⠹ thinking… 1s", "reasoning text", "> Ask anything, / for commands", "m"],
            frame.Lines
        );
        Assert.Equal(2, frame.CursorRow);
    }

    [Fact]
    public void Thinking_keeps_the_newest_lines_within_the_twelve_line_cap()
    {
        string text = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"thought {i}"));

        var frame = LiveAreaRenderer.Render(WithThinking(text, elapsed: 2));

        Assert.Equal(13, frame.Lines.Count);
        Assert.Equal("⠹ thinking… 2s", frame.Lines[0]);
        Assert.Equal("thought 10", frame.Lines[1]);
        Assert.Equal("thought 20", frame.Lines[^2]);
        Assert.Equal("> Ask anything, / for commands", frame.Lines[^1]);
    }

    [Fact]
    public void Thinking_text_wraps_at_the_width()
    {
        var frame = LiveAreaRenderer.Render(
            WithThinking("012345678901234567890123456789", width: 20)
        );

        Assert.Equal(
            ["⠹ thinking… 0s", "01234567890123456789", "0123456789", "> Ask anything, / fo"],
            frame.Lines
        );
    }

    [Fact]
    public void Thinking_lines_carry_the_thinking_colour_when_the_terminal_supports_colour()
    {
        var truecolor = LiveAreaRenderer.Render(
            WithThinking("reasoning text", color: ColorSystemSupport.TrueColor)
        );
        var sixteen = LiveAreaRenderer.Render(
            WithThinking("reasoning text", color: ColorSystemSupport.Detect)
        );

        Assert.Equal("⠹ thinking… 0s", truecolor.Lines[0]);
        Assert.Equal("\u001b[38;2;175;135;215mreasoning text\u001b[0m", truecolor.Lines[1]);
        Assert.Equal("\u001b[35mreasoning text\u001b[0m", sixteen.Lines[1]);
    }

    [Fact]
    public void Thinking_lines_stay_plain_and_unchanged_under_no_colours()
    {
        var frame = LiveAreaRenderer.Render(WithThinking("reasoning text"));
        var colored = LiveAreaRenderer.Render(
            WithThinking("reasoning text", color: ColorSystemSupport.TrueColor)
        );

        Assert.Equal(
            ["⠹ thinking… 0s", "reasoning text", "> Ask anything, / for commands"],
            frame.Lines
        );
        Assert.Equal(frame.Lines, colored.Lines.Select(StripSgr));
    }

    private static LiveAreaState WithThinking(
        string text,
        int elapsed = 0,
        int width = 40,
        ColorSystemSupport color = ColorSystemSupport.NoColors
    ) =>
        new()
        {
            Thinking = new ThinkingState { Text = text, ElapsedSeconds = elapsed },
            Color = color,
            Size = new ConsoleSize(width, 24),
        };

    private static string StripSgr(string line) =>
        Regex.Replace(line, "\u001b\\[[0-9;]*m", string.Empty);
}
