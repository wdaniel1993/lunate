using Spectre.Console;
using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

/// <summary>
/// The transcript viewer's full-screen frame: exactly one line per row, the newest content at the
/// bottom on open, full tool outputs and diffs, full thinking text, mid-shortened paths, and the
/// footer's keys and position. The viewer is pure — same transcript, size and scroll offset, same
/// frame.
/// </summary>
public sealed class TranscriptViewerRendererTests
{
    private static readonly ConsoleSize Size = new(40, 10);

    [Fact]
    public void The_frame_has_exactly_the_requested_rows() =>
        Assert.Equal(Size.Rows, Viewer().Lines(LongTranscript(), Size, 0).Count);

    [Fact]
    public void A_degenerate_size_still_produces_one_line() =>
        Assert.Single(Viewer().Lines(LongTranscript(), new ConsoleSize(0, 0), 0));

    [Fact]
    public void An_empty_transcript_shows_only_blanks_and_the_footer_at_100_percent()
    {
        var frame = Viewer().Lines(new TranscriptModel([]), Size, 0);

        Assert.Equal(Size.Rows, frame.Count);
        Assert.All(frame.Take(Size.Rows - 1), line => Assert.True(line.IsBlank));
        Assert.EndsWith("100%", Text(frame[^1]), StringComparison.Ordinal);
    }

    [Fact]
    public void The_view_opens_at_the_latest_content()
    {
        var frame = Viewer().Lines(LongTranscript(), Size, 0);

        Assert.Equal("step 30 finished", Text(frame[^2]));
        Assert.EndsWith("100%", Text(frame[^1]), StringComparison.Ordinal);
    }

    [Fact]
    public void Scrolling_up_moves_the_window_up_and_lowers_the_percent()
    {
        var frame = Viewer().Lines(LongTranscript(), Size, scrolledLines: 1);

        Assert.Equal("step 29 finished", Text(frame[^2]));
        Assert.EndsWith("95%", Text(frame[^1]), StringComparison.Ordinal);
    }

    [Fact]
    public void A_negative_scroll_clamps_to_the_latest_content()
    {
        var frame = Viewer().Lines(LongTranscript(), Size, scrolledLines: -5);

        Assert.Equal("step 30 finished", Text(frame[^2]));
        Assert.EndsWith("100%", Text(frame[^1]), StringComparison.Ordinal);
    }

    [Fact]
    public void Scrolling_beyond_the_start_clamps_to_the_top()
    {
        var frame = Viewer().Lines(LongTranscript(), Size, scrolledLines: 1000);

        Assert.Equal("\u2713 bash \u00b7 exit 0 \u00b7 0.4s", Text(frame[0]));
        Assert.EndsWith("0%", Text(frame[^1]), StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_header_at_40_columns_drops_the_command_summary()
    {
        var frame = Viewer().Lines(LongTranscript(), new ConsoleSize(40, 10), scrolledLines: 1000);

        Assert.Equal("\u2713 bash \u00b7 exit 0 \u00b7 0.4s", Text(frame[0]));
    }

    [Fact]
    public void A_tool_header_at_60_columns_keeps_the_command_summary()
    {
        var frame = Viewer().Lines(LongTranscript(), new ConsoleSize(60, 10), scrolledLines: 1000);

        Assert.Equal("\u2713 bash  dotnet build \u00b7 exit 0 \u00b7 0.4s", Text(frame[0]));
    }

    [Fact]
    public void A_long_output_is_fully_visible_without_the_elision_marker()
    {
        var model = new TranscriptModel([
            new TranscriptToolEntry(
                new ToolBlockModel(
                    "read",
                    """{"path":"src/Long.cs"}""",
                    ToolBlockStatus.Ok,
                    NumberedLines(30),
                    null
                )
            ),
        ]);

        string[] frame = Viewer().Lines(model, new ConsoleSize(60, 40), 0).Select(Text).ToArray();

        Assert.Contains("line 01", frame);
        Assert.Contains("line 30", frame);
        Assert.DoesNotContain(frame, line => line.Contains("ctrl+o", StringComparison.Ordinal));
    }

    [Fact]
    public void The_diff_body_is_visible()
    {
        var model = new TranscriptModel([new TranscriptToolEntry(EditBlock())]);

        string[] frame = Viewer().Lines(model, new ConsoleSize(60, 20), 0).Select(Text).ToArray();

        Assert.Contains("-    _count = _count + 1;", frame);
        Assert.Contains("+    _count++;", frame);
    }

    [Fact]
    public void A_thinking_entry_shows_its_full_text_even_when_the_commit_is_collapsed()
    {
        var commit = new ThinkingCommitModel(
            "We need to weigh the options carefully before we decide",
            12,
            Full: false
        );
        var model = new TranscriptModel([new TranscriptThinkingEntry(commit)]);

        string[] frame = Viewer().Lines(model, new ConsoleSize(60, 20), 0).Select(Text).ToArray();

        Assert.Contains(
            frame,
            line => line.Contains("weigh the options", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            frame,
            line => line.Contains("thought for", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void A_blank_thinking_text_falls_back_to_the_collapsed_line()
    {
        var commit = new ThinkingCommitModel("   ", 12, Full: false);
        var model = new TranscriptModel([new TranscriptThinkingEntry(commit)]);

        string[] frame = Viewer().Lines(model, new ConsoleSize(60, 20), 0).Select(Text).ToArray();

        Assert.Contains(
            frame,
            line => line.Contains("\u2726 thought for 12s", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void An_assistant_entry_renders_through_the_markdown_renderer()
    {
        var model = new TranscriptModel([new TranscriptAssistantEntry("**bold** text")]);

        var frame = Viewer().Lines(model, new ConsoleSize(40, 10), 0);

        Assert.Equal(
            new MarkdownRenderer().Lines("**bold** text")[0].ToMarkup(),
            frame[0].ToMarkup()
        );
    }

    [Fact]
    public void A_user_entry_carries_the_echo_prefix()
    {
        var model = new TranscriptModel([new TranscriptUserEntry("hey")]);

        Assert.Equal("[dim]\u00bb hey[/]", Viewer().Lines(model, Size, 0)[0].ToMarkup());
    }

    [Fact]
    public void Ascii_capabilities_use_the_ascii_glyphs()
    {
        var viewer = new TranscriptViewerRenderer(
            new TerminalCapabilities(ColorSystemSupport.NoColors, Unicode: false)
        );
        var model = new TranscriptModel([new TranscriptUserEntry("hey")]);

        var frame = viewer.Lines(model, Size, 0);

        Assert.Equal("> hey", Text(frame[0]));
        Assert.StartsWith("up/down scroll", Text(frame[^1]), StringComparison.Ordinal);
    }

    [Fact]
    public void Ascii_capabilities_use_ascii_separators_in_the_footer()
    {
        var viewer = new TranscriptViewerRenderer(
            new TerminalCapabilities(ColorSystemSupport.NoColors, Unicode: false)
        );
        var model = new TranscriptModel([new TranscriptUserEntry("hey")]);

        string footer = Text(viewer.Lines(model, new ConsoleSize(80, 10), 0)[^1]);

        Assert.StartsWith(
            "up/down scroll - PgUp/PgDn - Home/End - g/G - q close",
            footer,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("\u00b7", footer, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_path_is_mid_shortened_and_kept_within_the_columns()
    {
        var model = new TranscriptModel([
            new TranscriptToolEntry(
                new ToolBlockModel(
                    "read",
                    """{"path":"/Users/daniel/dev/lunate/src/Lunate.Tui/Transcript/TranscriptViewerRenderer.cs"}""",
                    ToolBlockStatus.Ok,
                    "one line",
                    null
                )
            ),
        ]);
        var viewer = new TranscriptViewerRenderer(
            new TerminalCapabilities(ColorSystemSupport.NoColors, Unicode: true),
            "/Users/daniel"
        );

        var frame = viewer.Lines(model, new ConsoleSize(60, 12), 0);

        Assert.Contains(
            frame,
            line =>
                Text(line).Contains("\u2026/TranscriptViewerRenderer.cs", StringComparison.Ordinal)
        );
        Assert.All(frame, line => Assert.True(CellText.Width(Text(line)) <= 60));
    }

    [Fact]
    public void Two_renders_of_the_same_input_are_equal()
    {
        var model = LongTranscript();
        var viewer = Viewer();

        Assert.Equal(
            viewer.Lines(model, Size, 3).Select(line => line.ToMarkup()),
            viewer.Lines(model, Size, 3).Select(line => line.ToMarkup())
        );
    }

    [Fact]
    public void Render_writes_the_frame_at_the_console_size()
    {
        var console = new TestConsole();
        console.Profile.Width = Size.Columns;
        console.Write(Viewer().Render(LongTranscript(), Size));

        string[] frame = console.Output.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

        Assert.Equal(Size.Rows, frame.Length);
        Assert.Contains("step 30 finished", frame[^2], StringComparison.Ordinal);
        Assert.EndsWith("100%", frame[^1], StringComparison.Ordinal);
    }

    private static TranscriptViewerRenderer Viewer() => new();

    private static string Text(StyledLine line) =>
        string.Concat(line.Spans.Select(span => span.Text));

    private static string NumberedLines(int count) =>
        string.Join('\n', Enumerable.Range(1, count).Select(n => $"line {n:00}"));

    /// <summary>A document taller than the frame, so every scroll position is meaningful.</summary>
    private static TranscriptModel LongTranscript() =>
        new([
            new TranscriptToolEntry(
                new ToolBlockModel(
                    "bash",
                    """{"command":"dotnet build"}""",
                    ToolBlockStatus.Ok,
                    string.Join('\n', Enumerable.Range(1, 30).Select(n => $"step {n:00} finished")),
                    null,
                    ExitCode: 0,
                    Duration: TimeSpan.FromMilliseconds(400)
                )
            ),
        ]);

    private static ToolBlockModel EditBlock() =>
        new(
            "edit",
            """{"path":"src/Counter.cs"}""",
            ToolBlockStatus.Ok,
            "edited src/Counter.cs lines 4\u20135 (match: exact)",
            new ToolDiffInfo(
                "src/Counter.cs",
                "exact",
                "--- a/src/Counter.cs\n"
                    + "+++ b/src/Counter.cs\n"
                    + "@@ -2,4 +2,4 @@\n"
                    + " public void Increment()\n"
                    + " {\n"
                    + "-    _count = _count + 1;\n"
                    + "+    _count++;\n"
                    + " }"
            )
        );
}
