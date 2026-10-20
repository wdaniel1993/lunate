using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// The input box and autocomplete list's committed snapshots (change <c>input-autocomplete</c>,
/// tasks 3.1/3.2/4.1): the empty placeholder, a filled line, a grown box that scrolls past eight
/// lines, command and file rows with their selection, and the closed no-match state — at
/// 120/80/60/50/40 columns in every colour mode the capability helper selects. Regenerate deliberately
/// with <c>LUNATE_TUI_UPDATE_GOLDENS=1 dotnet test --project tests/Lunate.Tui.Tests</c>.
/// </summary>
public sealed class InputFrameGoldenTests
{
    private static string GoldensDirectory =>
        Path.Combine(
            TestPaths.RepositoryRoot,
            "tests",
            "Lunate.Tui.Tests",
            "fixtures",
            "frames",
            "input"
        );

    private static readonly int[] Widths = [120, 80, 60, 50, 40];

    private static readonly string[] Modes = ["truecolor", "256-color", "16-color", "no-color"];

    private static readonly string[] States =
    [
        "box-empty",
        "box-filled",
        "box-multiline",
        "box-multiline-scroll",
        "list-command",
        "list-command-selected",
        "list-file",
        "list-file-selected",
        "list-no-match",
    ];

    public static TheoryData<string, int, string> Cases
    {
        get
        {
            var cases = new TheoryData<string, int, string>();
            foreach (string state in States)
            {
                foreach (int width in Widths)
                {
                    foreach (string mode in Modes)
                    {
                        cases.Add(state, width, mode);
                    }
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_input_frame_matches_the_committed_golden(string name, int width, string mode)
    {
        LiveAreaState state = State(name, width, mode);

        GoldenFiles.AssertMatchesLines(
            Path.Combine(GoldensDirectory, $"{name}-{width}-{mode}.txt"),
            LiveAreaRenderer.Render(state).Lines.Select(Escape).ToArray()
        );
    }

    [Theory]
    [InlineData("box-filled")]
    [InlineData("list-command-selected")]
    public void The_ascii_input_frame_matches_the_committed_golden(string name)
    {
        LiveAreaState state = State(name, 80, "no-color") with { Unicode = false };

        GoldenFiles.AssertMatchesLines(
            Path.Combine(GoldensDirectory, $"{name}-80-no-color-ascii.txt"),
            LiveAreaRenderer.Render(state).Lines.Select(Escape).ToArray()
        );
    }

    private static LiveAreaState State(string name, int width, string mode)
    {
        LiveAreaState state = new()
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
            Size = new ConsoleSize(width, 24),
        };

        return name switch
        {
            "box-empty" => state,
            "box-filled" => state with
            {
                Input = new InputLineState("Refactor the autocomplete list renderer.", 40),
            },
            "box-multiline" => state with
            {
                Input = new InputLineState(
                    "Fix the autocomplete list\nso the selected row\nstays inside the box.",
                    67
                ),
            },
            "box-multiline-scroll" => state with
            {
                Input = new InputLineState(
                    string.Join(
                        '\n',
                        Enumerable.Range(1, 12).Select(n => $"step {n:00} of the box")
                    ),
                    12 * "step 00 of the box".Length + 11
                ),
            },
            "list-command" => state with
            {
                Input = new InputLineState("/", 1),
                Autocomplete = CommandList(selected: 0),
            },
            "list-command-selected" => state with
            {
                Input = new InputLineState("/", 1),
                Autocomplete = CommandList(selected: 2),
            },
            "list-file" => state with
            {
                Input = new InputLineState("read @src/", 10),
                Autocomplete = FileList(selected: 0),
            },
            "list-file-selected" => state with
            {
                Input = new InputLineState("read @src/", 10),
                Autocomplete = FileList(selected: 1),
            },
            "list-no-match" => state with { Input = new InputLineState("/zzz", 4) },
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown state."),
        };
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

    private static AutocompleteState FileList(int selected) =>
        new(
            [
                CompletionItem.File("src/core/File.cs"),
                CompletionItem.File("src/core/Other.cs"),
                CompletionItem.File("src/main.cs"),
                CompletionItem.File("docs/guide.md"),
                CompletionItem.File("src/very/long/directory/name/Deep.cs"),
            ],
            selected,
            CompletionMode.File,
            TokenStart: 5
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

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\")
            .Replace("\u001b", "\\x1b")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
}
