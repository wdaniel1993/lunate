using Spectre.Console;
using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

/// <summary>
/// The tool blocks' committed snapshots (change <c>tool-blocks</c>, task 6.1): every scrollback
/// form at 120/80/50 columns and in every colour mode the capability helper selects (truecolor /
/// 256-colour / 16-colour / NO_COLOR). The running state lives in the live area and is not
/// snapshotted here. Regenerate deliberately with
/// <c>LUNATE_TUI_UPDATE_GOLDENS=1 dotnet test --project tests/Lunate.Tui.Tests</c>.
/// </summary>
public sealed class ToolBlocksFrameGoldenTests
{
    private static string GoldensDirectory =>
        Path.Combine(
            TestPaths.RepositoryRoot,
            "tests",
            "Lunate.Tui.Tests",
            "fixtures",
            "toolblocks"
        );

    private static readonly int[] Widths = [120, 80, 50];

    private static readonly string[] Modes = ["truecolor", "256-color", "16-color", "no-color"];

    public static TheoryData<string, int, string> Cases
    {
        get
        {
            var cases = new TheoryData<string, int, string>();
            foreach (string name in Blocks.Keys)
            {
                foreach (int width in Widths)
                {
                    foreach (string mode in Modes)
                    {
                        cases.Add(name, width, mode);
                    }
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_block_frame_matches_the_committed_golden(string name, int width, string mode)
    {
        var console = new TestConsole();
        console.Profile.Width = width;
        console.EmitAnsiSequences = true;
        console.Profile.Capabilities.ColorSystem = ConsoleColor(mode);
        console.Write(
            new ToolBlockRenderer(Capability(mode), homeDirectory: "/home/dev").Render(Blocks[name])
        );

        GoldenFiles.AssertMatchesLines(
            Path.Combine(GoldensDirectory, $"{name}-{width}-{mode}.txt"),
            console
                .Output.ReplaceLineEndings("\n")
                .TrimEnd('\n')
                .Split('\n')
                .Select(Escape)
                .ToArray()
        );
    }

    /// <summary>Every form once more without Unicode, pinning the ASCII separators and elisions.</summary>
    public static TheoryData<string> AsciiCases
    {
        get
        {
            var cases = new TheoryData<string>();
            foreach (string name in Blocks.Keys)
            {
                cases.Add(name);
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(AsciiCases))]
    public void The_ascii_block_frame_matches_the_committed_golden(string name)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.EmitAnsiSequences = true;
        console.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
        console.Write(
            new ToolBlockRenderer(
                new TerminalCapabilities(ColorSystemSupport.NoColors, Unicode: false),
                homeDirectory: "/home/dev"
            ).Render(Blocks[name])
        );

        GoldenFiles.AssertMatchesLines(
            Path.Combine(GoldensDirectory, $"{name}-80-ascii.txt"),
            console
                .Output.ReplaceLineEndings("\n")
                .TrimEnd('\n')
                .Split('\n')
                .Select(Escape)
                .ToArray()
        );
    }

    /// <summary>The expanded rendering once at the standard width without colour.</summary>
    public static TheoryData<string> ExpandedCases
    {
        get
        {
            var cases = new TheoryData<string>();
            foreach (string name in Blocks.Keys)
            {
                cases.Add(name);
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(ExpandedCases))]
    public void The_expanded_block_frame_matches_the_committed_golden(string name)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.EmitAnsiSequences = true;
        console.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
        console.Write(
            new ToolBlockRenderer(
                new TerminalCapabilities(ColorSystemSupport.NoColors, Unicode: true),
                homeDirectory: "/home/dev"
            ).Render(Blocks[name], 80, ToolDetails.Expanded)
        );

        GoldenFiles.AssertMatchesLines(
            Path.Combine(GoldensDirectory, $"{name}-80-no-color-expanded.txt"),
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

    /// <summary>One block per form; the running state lives in the live area and is excluded.</summary>
    private static readonly IReadOnlyDictionary<string, ToolBlockModel> Blocks = BuildBlocks();

    private static Dictionary<string, ToolBlockModel> BuildBlocks()
    {
        var diff = new ToolDiffInfo(
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
        );

        return new Dictionary<string, ToolBlockModel>(StringComparer.Ordinal)
        {
            ["read-ok"] = new ToolBlockModel(
                "read",
                """{"path":"src/Lunate.Tui/ToolBlocks/ToolBlockRenderer.cs"}""",
                ToolBlockStatus.Ok,
                "namespace Lunate.Tui;\n"
                    + "\n"
                    + "internal sealed class FrameWriter\n"
                    + "{\n"
                    + "    public void Write(string text) { }\n"
                    + "}",
                null
            ),
            ["read-image"] = new ToolBlockModel(
                "read",
                """{"path":"screens/diagram.png"}""",
                ToolBlockStatus.Ok,
                "[image 1024x768]",
                null
            ),
            ["read-no-output"] = new ToolBlockModel(
                "read",
                """{"path":"README.md"}""",
                ToolBlockStatus.Ok,
                "",
                null
            ),
            ["read-error"] = new ToolBlockModel(
                "read",
                """{"path":"src/missing.cs"}""",
                ToolBlockStatus.Error,
                "file not found: src/missing.cs",
                null
            ),
            ["bash-ok"] = new ToolBlockModel(
                "bash",
                """{"command":"dotnet test --project tests/Lunate.Tui.Tests"}""",
                ToolBlockStatus.Ok,
                "Test run summary: Passed!\n  total: 286\n  failed: 0\n  succeeded: 286",
                null,
                ExitCode: 0,
                Duration: TimeSpan.FromMilliseconds(400)
            ),
            ["bash-long-output"] = new ToolBlockModel(
                "bash",
                """{"command":"dotnet build"}""",
                ToolBlockStatus.Ok,
                string.Join('\n', Enumerable.Range(1, 30).Select(n => $"step {n:00} finished")),
                null,
                ExitCode: 0,
                Duration: TimeSpan.FromSeconds(62)
            ),
            ["edit-diff-exact"] = new ToolBlockModel(
                "edit",
                """{"path":"src/Counter.cs","old_text":"x","new_text":"y"}""",
                ToolBlockStatus.Ok,
                "edited src/Counter.cs lines 4\u20135 (match: exact)",
                diff
            ),
            ["edit-diff-normalized"] = new ToolBlockModel(
                "edit",
                """{"path":"src/Counter.cs","old_text":"x  ","new_text":"y"}""",
                ToolBlockStatus.Ok,
                "edited src/Counter.cs lines 4\u20134 (match: normalized)",
                new ToolDiffInfo(
                    "src/Counter.cs",
                    "normalized",
                    "--- a/src/Counter.cs\n"
                        + "+++ b/src/Counter.cs\n"
                        + "@@ -3,3 +3,3 @@\n"
                        + " {\n"
                        + "-    _count = _count + 1;  \n"
                        + "+    _count++;\n"
                        + " }"
                )
            ),
            ["write-diff"] = new ToolBlockModel(
                "write",
                """{"path":"src/NewFile.cs","content":"namespace App;"}""",
                ToolBlockStatus.Ok,
                "wrote 3 lines to src/NewFile.cs (created)",
                new ToolDiffInfo(
                    "src/NewFile.cs",
                    "exact",
                    "--- a/src/NewFile.cs\n"
                        + "+++ b/src/NewFile.cs\n"
                        + "@@ -0,0 +1,3 @@\n"
                        + "+namespace App;\n"
                        + "+\n"
                        + "+internal sealed class NewFile { }"
                )
            ),
            ["escaping"] = new ToolBlockModel(
                "edit",
                """{"path":"[red]src[/].cs"}""",
                ToolBlockStatus.Ok,
                "[dim]not markup[/]\n[bold]neither[/]\n[[nested]]\n",
                new ToolDiffInfo(
                    "[red]src[/].cs",
                    "[bold]exact[/]",
                    "--- a/[red]src[/].cs\n"
                        + "+++ b/[red]src[/].cs\n"
                        + "@@ -1 +1 @@\n"
                        + "-keep [dim]this[/]\n"
                        + "+keep [bold]that[/]"
                )
            ),
            ["cs-diagnostics"] = new ToolBlockModel(
                "cs_diagnostics",
                """{"scope":"changed"}""",
                ToolBlockStatus.Error,
                DiagnosticsOutput,
                null
            ),
            ["extension-fallback"] = new ToolBlockModel(
                "hello-tool",
                """{"directory":"/home/dev/dev/lunate/src/Lunate.Tui/ToolBlocks/ToolBlockRenderer.cs"}""",
                ToolBlockStatus.Ok,
                null,
                null
            ),
        };
    }

    private const string DiagnosticsOutput =
        "Partial load: one project failed to load.\n"
        + "2 errors, 1 warning in changed files.\n"
        + "src/a.cs(4,5): error CS0103: The name 'Foo' does not exist\n"
        + "src/b.cs(9,1): warning CS8600: Converting null literal\n"
        + "src/c.cs(1,1): error CS1002: ; expected";
}
