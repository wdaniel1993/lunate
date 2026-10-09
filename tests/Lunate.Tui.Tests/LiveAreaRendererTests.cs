namespace Lunate.Tui.Tests;

public sealed class LiveAreaRendererTests
{
    [Fact]
    public void Renders_tail_tool_footer_and_input_in_order()
    {
        var state = new LiveAreaState
        {
            Input = new InputLineState("go", 2),
            TailText = "line one\nline two",
            ToolName = "read src/foo.cs",
            Model = "test-model",
            InputTokens = 1000,
            OutputTokens = 200,
            ContextPercent = 12.5,
            WorkingDirectory = "/repo",
            GitBranch = "main",
            Size = new ConsoleSize(60, 12),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal(
            [
                "line one",
                "line two",
                "| read src/foo.cs",
                "test-model · 1200 tok · 12.5% ctx · /repo · main",
                "> go",
            ],
            frame.Lines
        );
        Assert.Equal(4, frame.CursorRow);
        Assert.Equal(4, frame.CursorColumn);
    }

    [Fact]
    public void Render_is_bounded_to_the_eight_line_area()
    {
        string tail = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"line {i}"));
        var state = new LiveAreaState { TailText = tail, Size = new ConsoleSize(20, 10) };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal(8, frame.Lines.Count);
        Assert.Equal("line 14", frame.Lines[0]);
        Assert.Equal("> ", frame.Lines[^1]);
        Assert.Equal(7, frame.CursorRow);
    }

    [Fact]
    public void Empty_state_renders_only_the_prompt()
    {
        var frame = LiveAreaRenderer.Render(new LiveAreaState { Size = new ConsoleSize(20, 8) });

        Assert.Equal(["> "], frame.Lines);
        Assert.Equal(0, frame.CursorRow);
        Assert.Equal(2, frame.CursorColumn);
    }

    [Fact]
    public void Footer_joins_only_populated_fields()
    {
        var state = new LiveAreaState
        {
            Model = "m",
            WorkingDirectory = "/w",
            Size = new ConsoleSize(60, 8),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal(["m · /w", "> "], frame.Lines);
    }

    [Fact]
    public void Context_percent_is_culture_invariant()
    {
        var state = new LiveAreaState { ContextPercent = 12.5, Size = new ConsoleSize(60, 8) };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Contains("12.5% ctx", frame.Lines);
    }

    [Fact]
    public void Tail_lines_are_clipped_to_the_width()
    {
        var state = new LiveAreaState { TailText = "abcdefghij", Size = new ConsoleSize(4, 8) };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal(["abcd", "> "], frame.Lines);
    }

    [Fact]
    public void Tool_line_shows_the_current_spinner_frame()
    {
        var state = new LiveAreaState
        {
            ToolName = "bash",
            FrameNumber = 1,
            Size = new ConsoleSize(20, 8),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Contains("/ bash", frame.Lines);
    }
}
