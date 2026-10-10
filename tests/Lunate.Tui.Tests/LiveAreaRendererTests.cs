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
            Footer = new StatusFooterModel("test-model", 1540, 12320, "/repo", "main"),
            Size = new ConsoleSize(60, 12),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal(
            [
                "line one",
                "line two",
                "| read src/foo.cs",
                "test-model · 1.5k/12.3k (13%) · /repo · main",
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
            Footer = new StatusFooterModel("m", 10, 0, "/w", null),
            Size = new ConsoleSize(60, 8),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal(["m · 10/0 · /w", "> "], frame.Lines);
    }

    [Fact]
    public void Notice_and_approval_lines_render_between_tool_and_footer()
    {
        var state = new LiveAreaState
        {
            Notice = "retrying (attempt 2)",
            Approval = new ApprovalPromptModel("bash", """{"command":"ls"}"""),
            Size = new ConsoleSize(60, 8),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal(
            ["retrying (attempt 2)", "Allow bash ls?  [y]es  [n]o  [a]lways this session", "> "],
            frame.Lines
        );
    }

    [Fact]
    public void Picker_lines_render_above_the_footer()
    {
        var state = new LiveAreaState
        {
            Picker = new SelectListModel("Select model", ["a", "b"], 0),
            Footer = new StatusFooterModel("m", 10, 0, "/w", null),
            Size = new ConsoleSize(60, 12),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal(["Select model", "> a", "  b", "m · 10/0 · /w", "> "], frame.Lines);
    }

    [Fact]
    public void The_picker_replaces_the_streaming_tail()
    {
        var state = new LiveAreaState
        {
            TailText = "streaming",
            Picker = new SelectListModel("Select model", ["a"], 0),
            Size = new ConsoleSize(60, 12),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal(["Select model", "> a", "> "], frame.Lines);
    }

    [Fact]
    public void Footer_context_percent_is_culture_invariant()
    {
        var state = new LiveAreaState
        {
            Footer = new StatusFooterModel("m", 1540, 12320, "/w", null),
            Size = new ConsoleSize(60, 8),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Contains(
            frame.Lines,
            line => line.Contains("1.5k/12.3k (13%)", StringComparison.Ordinal)
        );
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
