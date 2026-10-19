using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// The one activity line above the input: the thinking label, the active tool with its argument
/// summary, or the generic working label — plus the run's token deltas and the interrupt hint
/// while the turn is cancellable, within the width bands.
/// </summary>
public sealed class LiveAreaActivityRendererTests
{
    [Fact]
    public void Thinking_shows_its_elapsed_deltas_and_interrupt_hint()
    {
        var state = new LiveAreaState
        {
            Thinking = new ThinkingState { Text = "reasoning", ElapsedSeconds = 3 },
            Cancellable = true,
            RunInputTokens = 1200,
            RunOutputTokens = 180,
            Size = new ConsoleSize(80, 24),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal("⠹ thinking… 3s · ↑1.2k ↓180 · esc to interrupt", frame.Lines[0]);
    }

    [Fact]
    public void A_running_tool_shows_its_name_summary_elapsed_deltas_and_hint()
    {
        var state = new LiveAreaState
        {
            ToolName = "read",
            ToolSummary = "src/foo.cs",
            ToolElapsedSeconds = 4,
            FrameNumber = 2,
            Cancellable = true,
            RunInputTokens = 1200,
            RunOutputTokens = 180,
            Size = new ConsoleSize(80, 24),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal("- read src/foo.cs · 4s · ↑1.2k ↓180 · esc to interrupt", frame.Lines[0]);
    }

    [Fact]
    public void Generic_work_shows_the_working_label_elapsed_deltas_and_hint()
    {
        var state = new LiveAreaState
        {
            Running = true,
            RunElapsedSeconds = 7,
            FrameNumber = 1,
            Cancellable = true,
            RunInputTokens = 1200,
            RunOutputTokens = 180,
            Size = new ConsoleSize(80, 24),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal("/ working… 7s · ↑1.2k ↓180 · esc to interrupt", frame.Lines[0]);
    }

    [Fact]
    public void A_non_cancellable_turn_omits_the_deltas_and_the_hint()
    {
        var state = new LiveAreaState
        {
            Thinking = new ThinkingState { Text = "reasoning", ElapsedSeconds = 3 },
            RunInputTokens = 1200,
            RunOutputTokens = 180,
            Size = new ConsoleSize(80, 24),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal("⠹ thinking… 3s", frame.Lines[0]);
    }

    [Fact]
    public void Deltas_wait_for_the_first_usage_but_the_hint_shows()
    {
        var state = new LiveAreaState
        {
            Running = true,
            RunElapsedSeconds = 2,
            Cancellable = true,
            Size = new ConsoleSize(80, 24),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal("| working… 2s · esc to interrupt", frame.Lines[0]);
    }

    [Fact]
    public void The_narrow_band_keeps_spinner_label_and_elapsed_and_drops_the_extras()
    {
        var state = new LiveAreaState
        {
            ToolName = "bash",
            ToolSummary = "dotnet build",
            ToolElapsedSeconds = 4,
            Cancellable = true,
            RunInputTokens = 1200,
            RunOutputTokens = 180,
            Size = new ConsoleSize(40, 24),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal("| bash dotnet build · 4s", frame.Lines[0]);
        Assert.DoesNotContain("↑", frame.Lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("esc", frame.Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void The_metrics_appear_from_sixty_columns()
    {
        var state = new LiveAreaState
        {
            Thinking = new ThinkingState { Text = "reasoning", ElapsedSeconds = 3 },
            Cancellable = true,
            RunInputTokens = 1200,
            RunOutputTokens = 180,
        };

        string atFiftyNine = LiveAreaRenderer
            .Render(state with { Size = new ConsoleSize(59, 24) })
            .Lines[0];
        string atSixty = LiveAreaRenderer
            .Render(state with { Size = new ConsoleSize(60, 24) })
            .Lines[0];

        Assert.Equal("⠹ thinking… 3s", atFiftyNine);
        Assert.Equal("⠹ thinking… 3s · ↑1.2k ↓180 · esc to interrupt", atSixty);
    }

    [Theory]
    [InlineData(ColorSystemSupport.TrueColor)]
    [InlineData(ColorSystemSupport.Detect)]
    [InlineData(ColorSystemSupport.NoColors)]
    public void The_activity_line_carries_no_sgr(ColorSystemSupport color)
    {
        var state = new LiveAreaState
        {
            Thinking = new ThinkingState { Text = "reasoning", ElapsedSeconds = 3 },
            Cancellable = true,
            RunInputTokens = 1200,
            RunOutputTokens = 180,
            Color = color,
            Size = new ConsoleSize(80, 24),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal("⠹ thinking… 3s · ↑1.2k ↓180 · esc to interrupt", frame.Lines[0]);
        Assert.DoesNotContain("\u001b[", frame.Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_label_stays_one_physical_line()
    {
        var state = new LiveAreaState
        {
            ToolName = "read\tbad\nname",
            ToolSummary = "first\nsecond",
            ToolElapsedSeconds = 2,
            Size = new ConsoleSize(80, 24),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.All(frame.Lines, line => Assert.DoesNotContain('\n', line));
        Assert.All(frame.Lines, line => Assert.DoesNotContain('\r', line));
        Assert.Equal("| read bad name first second · 2s", frame.Lines[0]);
    }

    [Fact]
    public void An_empty_tool_summary_renders_only_the_name()
    {
        var state = new LiveAreaState
        {
            ToolName = "bash",
            ToolSummary = string.Empty,
            ToolElapsedSeconds = 2,
            Size = new ConsoleSize(80, 24),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal("| bash · 2s", frame.Lines[0]);
    }
}
