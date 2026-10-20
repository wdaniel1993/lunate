namespace Lunate.Tui.Tests;

public sealed class LiveAreaRendererTests
{
    [Fact]
    public void Renders_tail_tool_input_and_footer_in_order()
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

        string rule = new('─', 60);
        Assert.Equal(
            [
                "line one",
                "line two",
                "| read src/foo.cs · 0s",
                rule,
                "> go",
                rule,
                "test-model · 1.5k/12.3k (13%) · /repo · main",
            ],
            frame.Lines
        );
        Assert.Equal(4, frame.CursorRow);
        Assert.Equal(4, frame.CursorColumn);
    }

    [Fact]
    public void A_live_subagent_renders_a_status_line_below_the_tool_line()
    {
        var state = new LiveAreaState
        {
            ToolName = "subagents__spawn",
            SubagentRunning = true,
            Size = new ConsoleSize(60, 8),
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 60);
        Assert.Equal(
            [
                "| subagents__spawn · 0s",
                "subagent running",
                rule,
                "> Ask anything, / for commands",
                rule,
            ],
            frame.Lines
        );
    }

    [Fact]
    public void Render_is_bounded_to_the_eight_line_area()
    {
        string tail = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"line {i}"));
        var state = new LiveAreaState { TailText = tail, Size = new ConsoleSize(20, 10) };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal(8, frame.Lines.Count);
        Assert.Equal("line 15", frame.Lines[0]);
        Assert.Equal(new string('─', 20), frame.Lines[^2]);
        Assert.Equal("> Ask anything, / fo", frame.Lines[^1]);
        Assert.Equal(7, frame.CursorRow);
    }

    [Fact]
    public void Empty_state_renders_the_placeholder_at_the_prompt()
    {
        var frame = LiveAreaRenderer.Render(new LiveAreaState { Size = new ConsoleSize(20, 8) });

        Assert.Equal([new string('─', 20), "> Ask anything, / fo"], frame.Lines);
        Assert.Equal(1, frame.CursorRow);
        Assert.Equal(2, frame.CursorColumn);
    }

    [Fact]
    public void A_compact_footer_model_wider_than_the_terminal_is_clipped_deterministically()
    {
        const string model = "a-model-name-that-is-much-wider-than-forty-columns";
        var state = new LiveAreaState
        {
            Footer = new StatusFooterModel(model, 10, 100, "/w", null),
            Size = new ConsoleSize(40, 8),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal(CellText.Clip(model + " \u00b7 10%", 40), frame.Lines[^1]);
        Assert.True(CellText.Width(frame.Lines[^1]) <= 40);
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

        string rule = new('─', 60);
        Assert.Equal([rule, "> Ask anything, / for commands", rule, "m · 10/0 · /w"], frame.Lines);
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

        string rule = new('─', 60);
        Assert.Equal(
            [
                "retrying (attempt 2)",
                "\u26a0 bash  ls",
                "Allow bash?  [y]es  [n]o  [a]lways this session",
                rule,
                "> Ask anything, / for commands",
                rule,
            ],
            frame.Lines
        );
    }

    [Fact]
    public void A_multi_line_approval_panel_renders_above_the_input()
    {
        var state = new LiveAreaState
        {
            Input = new InputLineState("go", 2),
            Approval = new ApprovalPromptModel("bash", "{}", Detail: ["ls", "line one"]),
            Size = new ConsoleSize(60, 8),
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 60);
        Assert.Equal(
            [
                "\u26a0 bash  ls",
                "  line one",
                "Allow bash?  [y]es  [n]o  [a]lways this session",
                rule,
                "> go",
                rule,
            ],
            frame.Lines
        );
        Assert.Equal(4, frame.CursorRow);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(24)]
    public void A_full_approval_panel_fits_the_frame_at_twenty_and_twenty_four_rows(int rows)
    {
        var state = new LiveAreaState
        {
            ToolName = "bash",
            Notice = "retrying (attempt 2)",
            Approval = new ApprovalPromptModel(
                "bash",
                "{}",
                Detail: ["ls", "a", "b", "c", "d", "e", "f"]
            ),
            Footer = new StatusFooterModel("test-model", 1540, 12320, "/repo", "main"),
            Size = new ConsoleSize(80, rows),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.True(frame.Lines.Count <= rows);
        Assert.Contains("\u26a0 bash  ls", frame.Lines);
        Assert.Contains("  f", frame.Lines);
        Assert.Contains("Allow bash?  [y]es  [n]o  [a]lways this session", frame.Lines);
    }

    private const string Headline = "\u26a0 bash  echo hi";
    private const string Question = "Allow bash?  [y]es  [n]o  [a]lways this session";
    private const string Prompt = "> Ask anything, / for commands";
    private static readonly string Rule = new('\u2500', 80);

    public static TheoryData<int, string[], int> PanelTrimCases =>
        new()
        {
            { 6, [Headline, "  first", Question, Rule, Prompt, Rule], 4 },
            { 5, [Headline, Question, Rule, Prompt, Rule], 3 },
            { 4, [Question, Rule, Prompt, Rule], 2 },
        };

    [Theory]
    [MemberData(nameof(PanelTrimCases))]
    public void A_panel_above_the_input_trims_detail_before_the_headline(
        int rows,
        string[] expectedLines,
        int expectedCursorRow
    )
    {
        var state = new LiveAreaState
        {
            Approval = new ApprovalPromptModel(
                "bash",
                "{}",
                Detail: ["echo hi", "first", "second", "third", "fourth"]
            ),
            Size = new ConsoleSize(80, rows),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Contains(Question, frame.Lines);
        Assert.Equal(expectedLines, frame.Lines);
        Assert.Equal(expectedCursorRow, frame.CursorRow);
    }

    [Fact]
    public void A_narrow_frame_keeps_the_choices_line_whole()
    {
        const string choices = "[y]es  [n]o  [a]lways this session";
        var state = new LiveAreaState
        {
            Input = new InputLineState("go", 2),
            Approval = new ApprovalPromptModel("bash", """{"command":"ls"}"""),
            Size = new ConsoleSize(40, 8),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal(
            ["\u26a0 bash  ls", "Allow bash?", choices, new string('─', 40), "> go"],
            frame.Lines
        );
        Assert.Equal(34, CellText.Width(frame.Lines[2]));
    }

    [Fact]
    public void A_multiline_notice_renders_as_one_physical_line()
    {
        var state = new LiveAreaState
        {
            Notice = "MCP server 'x' failed: first\r\nsecond\nthird. Continuing.",
            Size = new ConsoleSize(120, 8),
        };

        var frame = LiveAreaRenderer.Render(state);

        string notice = Assert.Single(
            frame.Lines,
            line => line.Contains("MCP server", StringComparison.Ordinal)
        );
        Assert.Equal("MCP server 'x' failed: first second third. Continuing.", notice);
        Assert.DoesNotContain("\n", notice, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", notice, StringComparison.Ordinal);
    }

    [Fact]
    public void Trust_lines_render_above_the_box()
    {
        var state = new LiveAreaState
        {
            Trust = new TrustPromptModel(
                "Load project extension 'hello' 0.1.0 from /w",
                ["test — dotnet exec x.dll"],
                1,
                2
            ),
            Footer = new StatusFooterModel("m", 10, 0, "/w", null),
            Size = new ConsoleSize(120, 12),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal(
            [
                "Load project extension 'hello' 0.1.0 from /w?  1 of 2  [y]es  [n]o",
                "test — dotnet exec x.dll",
                new string('─', 120),
                "> Ask anything, / for commands",
                new string('─', 120),
                "m · 10/0 · /w",
            ],
            frame.Lines
        );
        Assert.Equal(3, frame.CursorRow);
    }

    [Fact]
    public void Loading_status_takes_the_footer_slot_until_cleared()
    {
        var state = new LiveAreaState
        {
            Loading = "loading project…",
            Footer = new StatusFooterModel("m", 10, 0, "/w", null),
            Size = new ConsoleSize(60, 8),
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 60);
        Assert.Equal(
            [rule, "> Ask anything, / for commands", rule, "loading project…"],
            frame.Lines
        );
    }

    [Fact]
    public void Picker_lines_render_above_the_box()
    {
        var state = new LiveAreaState
        {
            Picker = new SelectListModel("Select model", ["a", "b"], 0),
            Footer = new StatusFooterModel("m", 10, 0, "/w", null),
            Size = new ConsoleSize(60, 12),
        };

        var frame = LiveAreaRenderer.Render(state);

        string rule = new('─', 60);
        Assert.Equal(
            [
                "Select model",
                "> a",
                "  b",
                rule,
                "> Ask anything, / for commands",
                rule,
                "m · 10/0 · /w",
            ],
            frame.Lines
        );
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

        string rule = new('─', 60);
        Assert.Equal(
            ["Select model", "> a", rule, "> Ask anything, / for commands", rule],
            frame.Lines
        );
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

        Assert.Equal(["abcd", "────", "> As"], frame.Lines);
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

        Assert.Contains("/ bash · 0s", frame.Lines);
    }
}
