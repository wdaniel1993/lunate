using Microsoft.Reactive.Testing;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui.Tests;

/// <summary>
/// The width invariant at every matrix size: the live area and a committed turn's scrollback
/// blocks never paint a line wider than the terminal, with long summaries and diffs included.
/// </summary>
public sealed class NarrowFrameInvariantTests
{
    public static TheoryData<int, int> Sizes =>
        new()
        {
            { 40, 20 },
            { 60, 30 },
            { 80, 24 },
            { 120, 40 },
        };

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Every_live_area_line_fits_the_terminal(int width, int height)
    {
        var state = new LiveAreaState
        {
            Size = new ConsoleSize(width, height),
            Footer = new StatusFooterModel(
                "deepseek-v4.1-flash",
                12400,
                128000,
                "~/dev/lunate",
                "main"
            ),
            Input = new InputLineState(
                "a long prompt that keeps going well past the narrowest terminal width",
                70
            ),
            TailText =
                "a committed paragraph with a line that is deliberately longer than the terminal",
            ToolName = "bash dotnet test --project tests/Lunate.Coding.Tests",
            SubagentRunning = true,
            Thinking = new ThinkingState
            {
                Text =
                    "reasoning that is longer than the narrowest terminal width by a good margin",
                ElapsedSeconds = 3,
            },
        };

        RenderedFrame frame = LiveAreaRenderer.Render(state);

        Assert.All(
            frame.Lines,
            line =>
                Assert.True(
                    CellText.Width(line) <= width,
                    $"'{line}' is wider than {width} columns"
                )
        );
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Every_committed_scrollback_line_fits_the_terminal(int width, int height)
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(width, height))
        {
            IsInteractive = false,
        };
        using var area = new LiveArea(console, scheduler);
        var renderer = new ToolBlockRenderer(
            new TerminalCapabilities(ColorSystemSupport.NoColors, Unicode: true),
            homeDirectory: "/home/dev"
        );

        foreach (ToolBlockModel block in CommittedTurn())
        {
            IRenderable rendered = renderer.Render(block, width);
            area.WriteScrollback(scrollback => scrollback.Write(rendered));
        }

        string[] lines = string.Concat(console.Writes)
            .ReplaceLineEndings("\n")
            .TrimEnd('\n')
            .Split('\n');
        Assert.All(
            lines,
            line =>
                Assert.True(
                    CellText.Width(line) <= width,
                    $"'{line}' is wider than {width} columns"
                )
        );
    }

    /// <summary>A turn with the long shapes that put pressure on the width: a deep path, a long
    /// command and duration, and a diff with a line far past every matrix width.</summary>
    private static IReadOnlyList<ToolBlockModel> CommittedTurn() =>
        [
            new ToolBlockModel(
                "read",
                """{"path":"/home/dev/dev/lunate/src/Lunate.Tui/ToolBlocks/ToolBlockRenderer.cs"}""",
                ToolBlockStatus.Ok,
                string.Join('\n', Enumerable.Range(1, 30).Select(n => $"line {n:00} of the file")),
                null
            ),
            new ToolBlockModel(
                "bash",
                """{"command":"dotnet test --project tests/Lunate.Coding.Tests --filter ToolBlock"}""",
                ToolBlockStatus.Ok,
                "Test run summary: Passed!",
                null,
                ExitCode: 0,
                Duration: TimeSpan.FromSeconds(62)
            ),
            new ToolBlockModel(
                "bash",
                """{"command":"wc -m report.txt"}""",
                ToolBlockStatus.Ok,
                new string('\u6f22', 60),
                null,
                ExitCode: 0,
                Duration: TimeSpan.FromSeconds(1)
            ),
            new ToolBlockModel(
                "edit",
                """{"path":"src/Lunate.Tui/ToolBlocks/ToolBlockRenderer.cs"}""",
                ToolBlockStatus.Ok,
                "edited",
                new ToolDiffInfo(
                    "src/Lunate.Tui/ToolBlocks/ToolBlockRenderer.cs",
                    "exact",
                    "--- a/src/ToolBlockRenderer.cs\n"
                        + "+++ b/src/ToolBlockRenderer.cs\n"
                        + "@@ -1,3 +1,3 @@\n"
                        + "-"
                        + new string('x', 200)
                        + "\n+"
                        + new string('y', 200)
                )
            ),
        ];
}
