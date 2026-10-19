using Spectre.Console;
using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

/// <summary>
/// The viewer's committed snapshots (change <c>tool-blocks</c>, task 6.2): the 24-row frame that
/// opens at the latest content — the transcript's tail — at 120/80/50 columns and in every colour
/// mode the capability helper selects (truecolor / 256-colour / 16-colour / NO_COLOR). The entry
/// forms above the visible tail are pinned by <see cref="TranscriptViewerRendererTests"/>.
/// Regenerate deliberately with
/// <c>LUNATE_TUI_UPDATE_GOLDENS=1 dotnet test --project tests/Lunate.Tui.Tests</c>.
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

    private static readonly int[] Widths = [120, 80, 50];

    private static readonly string[] Modes = ["truecolor", "256-color", "16-color", "no-color"];

    public static TheoryData<int, string> Cases
    {
        get
        {
            var cases = new TheoryData<int, string>();
            foreach (int width in Widths)
            {
                foreach (string mode in Modes)
                {
                    cases.Add(width, mode);
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_viewer_frame_matches_the_committed_golden(int width, string mode)
    {
        var console = new TestConsole();
        console.Profile.Width = width;
        console.EmitAnsiSequences = true;
        console.Profile.Capabilities.ColorSystem = ConsoleColor(mode);
        console.Write(
            new TranscriptViewerRenderer(Capability(mode), homeDirectory: "/home/dev").Render(
                FixtureTranscript(),
                new ConsoleSize(width, 24)
            )
        );

        GoldenFiles.AssertMatchesLines(
            Path.Combine(GoldensDirectory, $"viewer-{width}-{mode}.txt"),
            console
                .Output.ReplaceLineEndings("\n")
                .TrimEnd('\n')
                .Split('\n')
                .Select(Escape)
                .ToArray()
        );
    }

    [Fact]
    public void The_ascii_viewer_frame_matches_the_committed_golden()
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.EmitAnsiSequences = true;
        console.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
        console.Write(
            new TranscriptViewerRenderer(
                new TerminalCapabilities(ColorSystemSupport.NoColors, Unicode: false),
                homeDirectory: "/home/dev"
            ).Render(FixtureTranscript(), new ConsoleSize(80, 24))
        );

        GoldenFiles.AssertMatchesLines(
            Path.Combine(GoldensDirectory, "viewer-80-ascii.txt"),
            console
                .Output.ReplaceLineEndings("\n")
                .TrimEnd('\n')
                .Split('\n')
                .Select(Escape)
                .ToArray()
        );
    }

    /// <summary>The capability from the detection seam; Standard is the 16-colour bucket.</summary>
    private static TerminalCapabilities Capability(string mode) =>
        new(
            mode switch
            {
                "truecolor" => ColorSystemSupport.TrueColor,
                "256-color" => ColorSystemSupport.EightBit,
                "16-color" => ColorSystemSupport.Standard,
                _ => ColorSystemSupport.NoColors,
            },
            Unicode: true
        );

    /// <summary>The console a session that detected that capability would have.</summary>
    private static ColorSystem ConsoleColor(string mode) =>
        ConsoleColorSystem.Resolve(Capability(mode).Color);

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\")
            .Replace("\u001b", "\\x1b")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");

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
                    """{"path":"/home/dev/dev/lunate/src/Lunate.Tui/Transcript/TranscriptViewerRenderer.cs"}""",
                    ToolBlockStatus.Ok,
                    string.Join(
                        '\n',
                        Enumerable.Range(1, 2).Select(n => $"line {n:00} of the file")
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
