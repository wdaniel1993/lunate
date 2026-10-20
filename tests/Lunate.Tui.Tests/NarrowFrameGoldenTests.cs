using Spectre.Console;
using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

/// <summary>
/// The narrow-terminals snapshot matrix (change <c>narrow-terminals</c>, task 3.1): an idle frame
/// after a committed turn (tool blocks with a long summary), the empty and filled input box, the
/// command list with a selection, thinking, and the working indicator — each at 40×20, 60×30,
/// 80×24 and 120×40 in every colour mode the capability helper selects. The state carries fixed
/// elapsed seconds and the spinner a fixed frame number, so the goldens are clock-free.
/// Regenerate deliberately with
/// <c>LUNATE_TUI_UPDATE_GOLDENS=1 dotnet test --project tests/Lunate.Tui.Tests</c>.
/// </summary>
public sealed class NarrowFrameGoldenTests
{
    private static string GoldensDirectory =>
        Path.Combine(
            TestPaths.RepositoryRoot,
            "tests",
            "Lunate.Tui.Tests",
            "fixtures",
            "frames",
            "narrow"
        );

    private static readonly (int Width, int Height)[] Sizes =
    [
        (40, 20),
        (60, 30),
        (80, 24),
        (120, 40),
    ];

    private static readonly string[] Modes = ["truecolor", "256-color", "16-color", "no-color"];

    private static readonly string[] States =
    [
        "committed-turn",
        "box-empty",
        "box-filled",
        "list-command-selected",
        "thinking",
        "working",
    ];

    public static TheoryData<string, int, int, string> Cases
    {
        get
        {
            var cases = new TheoryData<string, int, int, string>();
            foreach (string state in States)
            {
                foreach ((int width, int height) in Sizes)
                {
                    foreach (string mode in Modes)
                    {
                        cases.Add(state, width, height, mode);
                    }
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_narrow_frame_matches_the_committed_golden(
        string state,
        int width,
        int height,
        string mode
    )
    {
        string[] lines = Render(state, width, height, mode);

        Assert.True(
            lines.Length <= height,
            $"{state} at {width}x{height} renders {lines.Length} rows"
        );
        GoldenFiles.AssertMatchesLines(
            Path.Combine(GoldensDirectory, $"{state}-{width}x{height}-{mode}.txt"),
            lines
        );
    }

    private static string[] Render(string state, int width, int height, string mode)
    {
        var lines = new List<string>();
        if (state == "committed-turn")
        {
            lines.AddRange(Scrollback(width, height, mode));
        }

        lines.AddRange(Live(state, width, height, mode));
        return [.. lines.Select(Escape)];
    }

    /// <summary>The committed turn: a deep read path, a long command with a duration, and a diff.</summary>
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
                "edit",
                """{"path":"src/Lunate.Tui/ToolBlocks/ToolBlockRenderer.cs"}""",
                ToolBlockStatus.Ok,
                "edited src/Lunate.Tui/ToolBlocks/ToolBlockRenderer.cs lines 4\u20135 (match: exact)",
                new ToolDiffInfo(
                    "src/Lunate.Tui/ToolBlocks/ToolBlockRenderer.cs",
                    "exact",
                    "--- a/src/ToolBlockRenderer.cs\n"
                        + "+++ b/src/ToolBlockRenderer.cs\n"
                        + "@@ -2,4 +2,4 @@\n"
                        + " public void Write(string text)\n"
                        + " {\n"
                        + "-    _builder.Append(text);\n"
                        + "+    _builder.AppendLine(text);\n"
                        + " }"
                )
            ),
        ];

    private static string[] Scrollback(int width, int height, string mode)
    {
        var console = new TestConsole();
        console.Profile.Width = width;
        console.Profile.Height = height;
        console.EmitAnsiSequences = true;
        console.Profile.Capabilities.ColorSystem = ConsoleColor(mode);
        var renderer = new ToolBlockRenderer(
            new TerminalCapabilities(Capability(mode), Unicode: true),
            homeDirectory: "/home/dev"
        );

        foreach (ToolBlockModel block in CommittedTurn())
        {
            // This matrix pins the detailed layout, so it asks for the expanded form; the
            // scrollback's default collapsed form has its own tool-block matrix.
            console.Write(renderer.Render(block, width, ToolDetails.Expanded));
        }

        return console.Output.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
    }

    private static string[] Live(string state, int width, int height, string mode)
    {
        LiveAreaState live = new()
        {
            Color = Capability(mode),
            Unicode = true,
            Footer = new StatusFooterModel(
                "deepseek-v4.1-flash",
                2100,
                1_000_000,
                "~/dev/lunate",
                "main"
            ),
            Size = new ConsoleSize(width, height),
        };

        live = state switch
        {
            "committed-turn" or "box-empty" => live,
            "box-filled" => live with
            {
                Input = new InputLineState("Refactor the autocomplete list renderer.", 40),
            },
            "list-command-selected" => live with
            {
                Input = new InputLineState("/", 1),
                Autocomplete = CommandList(selected: 2),
            },
            "thinking" => live with
            {
                Thinking = new ThinkingState
                {
                    Text =
                        "We need to add validation to TodoList.Add. Null, empty and "
                        + "whitespace-only titles must throw ArgumentException. The check belongs "
                        + "at the top of the method, before the list mutates.",
                    ElapsedSeconds = 6,
                },
            },
            "working" => live with
            {
                ToolName = "bash dotnet test --project tests/Lunate.Coding.Tests",
                FrameNumber = 1,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown state."),
        };

        return [.. LiveAreaRenderer.Render(live).Lines];
    }

    private static AutocompleteState CommandList(int selected) =>
        new(
            [
                new("/compact", "compact the conversation now", CompletionKind.Command, null),
                new(
                    "/context",
                    "show the context window and subagent budgets",
                    CompletionKind.Command,
                    null
                ),
                new("/help", "list the commands", CompletionKind.Command, null),
                new("/new", "start a fresh session", CompletionKind.Command, null),
                new("/resume", "open the session picker", CompletionKind.Command, null),
            ],
            selected,
            CompletionMode.Command,
            TokenStart: 0
        );

    /// <summary>The capability from the detection seam; Detect is the 16-colour bucket.</summary>
    private static ColorSystemSupport Capability(string mode) =>
        mode switch
        {
            "truecolor" => ColorSystemSupport.TrueColor,
            "256-color" => ColorSystemSupport.EightBit,
            "16-color" => ColorSystemSupport.Detect,
            _ => ColorSystemSupport.NoColors,
        };

    /// <summary>The console a session that detected that capability would have.</summary>
    private static ColorSystem ConsoleColor(string mode) =>
        mode switch
        {
            "truecolor" => ColorSystem.TrueColor,
            "256-color" => ColorSystem.EightBit,
            "16-color" => ColorSystem.Standard,
            _ => ColorSystem.NoColors,
        };

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\")
            .Replace("\u001b", "\\x1b")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
}
