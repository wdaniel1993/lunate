using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

/// <summary>
/// The viewer's plain-text frames at the 120/80/50 breakpoints (24 rows). The full breakpoint x
/// colour-mode matrix — and the ANSI forms — is task 6.2; these goldens pin the layout: the frame
/// opens at the latest content, so a 24-row window shows the transcript's tail (the entry forms
/// above it are pinned by <see cref="TranscriptViewerRendererTests"/>). Regenerate deliberately
/// with <c>LUNATE_TUI_UPDATE_GOLDENS=1 dotnet test --project tests/Lunate.Tui.Tests</c>.
/// </summary>
public sealed class TranscriptViewerGoldenTests
{
    private static string GoldensDirectory =>
        Path.Combine(
            TestPaths.RepositoryRoot,
            "tests",
            "Lunate.Tui.Tests",
            "fixtures",
            "frames",
            "viewer"
        );

    public static TheoryData<int> Widths => new() { 120, 80, 50 };

    [Theory]
    [MemberData(nameof(Widths))]
    public void The_viewer_frame_matches_the_committed_golden(int width)
    {
        var console = new TestConsole();
        console.Profile.Width = width;
        console.Write(
            new TranscriptViewerRenderer().Render(FixtureTranscript(), new ConsoleSize(width, 24))
        );

        GoldenFiles.AssertMatchesLines(
            Path.Combine(GoldensDirectory, $"viewer-{width}.txt"),
            console.Output.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n')
        );
    }

    /// <summary>One transcript carrying every entry form the viewer renders.</summary>
    private static TranscriptModel FixtureTranscript() =>
        new([
            new TranscriptUserEntry("Add validation to TodoList.Add"),
            new TranscriptAssistantEntry(
                "Null, empty and whitespace-only titles must throw. The check belongs at the top of "
                    + "the method, before the list mutates."
            ),
            new TranscriptThinkingEntry(
                new ThinkingCommitModel(
                    "We need to add validation to TodoList.Add. Null, empty and whitespace-only "
                        + "titles must throw ArgumentException.",
                    12,
                    Full: false
                )
            ),
            new TranscriptToolEntry(
                new ToolBlockModel(
                    "read",
                    """{"path":"src/Lunate.Tui/Transcript/TranscriptViewerRenderer.cs"}""",
                    ToolBlockStatus.Ok,
                    string.Join(
                        '\n',
                        Enumerable.Range(1, 30).Select(n => $"line {n:00} of the file")
                    ),
                    null
                )
            ),
            new TranscriptToolEntry(
                new ToolBlockModel(
                    "edit",
                    """{"path":"src/TodoList.cs","old_text":"_items.Add(title)","new_text":"throw"}""",
                    ToolBlockStatus.Ok,
                    "edited src/TodoList.cs lines 4\u20135 (match: exact)",
                    new ToolDiffInfo(
                        "src/TodoList.cs",
                        "exact",
                        "--- a/src/TodoList.cs\n"
                            + "+++ b/src/TodoList.cs\n"
                            + "@@ -2,4 +2,4 @@\n"
                            + " public void Add(string title)\n"
                            + " {\n"
                            + "-    _items.Add(title);\n"
                            + "+    throw new ArgumentException(nameof(title));\n"
                            + " }"
                    )
                )
            ),
            new TranscriptToolEntry(
                new ToolBlockModel(
                    "bash",
                    """{"command":"dotnet test --project tests/Lunate.Coding.Tests"}""",
                    ToolBlockStatus.Ok,
                    "Test run summary: Passed!\n  total: 286\n  failed: 0\n  succeeded: 286",
                    null,
                    ExitCode: 0,
                    Duration: TimeSpan.FromMilliseconds(400)
                )
            ),
        ]);
}
