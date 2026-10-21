using Spectre.Console;
using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

/// <summary>
/// The turn snapshots (change <c>turn-echo</c>, task 4.1): the committed user echo (short and
/// wrapped) and the activity line (thinking with metrics, a running tool with its summary, and
/// generic work) at 40×20, 60×30, 80×24 and 120×40 in every colour mode. The state carries fixed
/// elapsed seconds, token deltas and spinner frames, so the goldens are clock-free.
/// Regenerate deliberately with
/// <c>LUNATE_TUI_UPDATE_GOLDENS=1 dotnet test --project tests/Lunate.Tui.Tests</c>.
/// </summary>
public sealed class TurnFrameGoldenTests
{
    private const string ShortMessage = "Add validation to TodoList.Add.";

    private const string ImageMessage = "explain [image 1024x768]";

    private const string WrappedMessage =
        "Add validation to TodoList.Add: reject null, empty and whitespace-only titles with "
        + "ArgumentException, and keep the guard at the top so the list never mutates on bad input.";

    private static string GoldensDirectory =>
        Path.Combine(
            TestPaths.RepositoryRoot,
            "tests",
            "Lunate.Tui.Tests",
            "fixtures",
            "frames",
            "turns"
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
        "echo-short",
        "echo-image",
        "echo-wrapped",
        "activity-thinking",
        "activity-tool",
        "activity-working",
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
    public void The_turn_frame_matches_the_committed_golden(
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

    private static string[] Render(string state, int width, int height, string mode) =>
        state switch
        {
            "echo-short" => Echo(ShortMessage, width, mode),
            "echo-image" => Echo(ImageMessage, width, mode),
            "echo-wrapped" => Echo(WrappedMessage, width, mode),
            "activity-thinking" => Activity(
                new LiveAreaState
                {
                    Thinking = new ThinkingState { Text = string.Empty, ElapsedSeconds = 3 },
                    Cancellable = true,
                    RunInputTokens = 1200,
                    RunOutputTokens = 180,
                },
                width,
                height,
                mode
            ),
            "activity-tool" => Activity(
                new LiveAreaState
                {
                    ToolName = "read",
                    ToolSummary = "src/Foo.cs",
                    ToolElapsedSeconds = 4,
                    FrameNumber = 2,
                    Cancellable = true,
                    RunInputTokens = 1200,
                    RunOutputTokens = 180,
                },
                width,
                height,
                mode
            ),
            "activity-working" => Activity(
                new LiveAreaState
                {
                    Running = true,
                    RunElapsedSeconds = 7,
                    FrameNumber = 1,
                    Cancellable = true,
                    RunInputTokens = 1200,
                    RunOutputTokens = 180,
                },
                width,
                height,
                mode
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown state."),
        };

    private static string[] Echo(string text, int width, string mode)
    {
        var console = new TestConsole();
        console.Profile.Width = width;
        console.EmitAnsiSequences = true;
        console.Profile.Capabilities.ColorSystem = ConsoleColor(mode);
        console.Write(new UserEchoRenderer().Render(text, width, Capability(mode)));
        return Lines(console.Output);
    }

    private static string[] Activity(LiveAreaState state, int width, int height, string mode)
    {
        state = state with
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

        return [.. LiveAreaRenderer.Render(state).Lines.Select(Escape)];
    }

    /// <summary>Split the console output, keeping the echo's one trailing blank line.</summary>
    private static string[] Lines(string output)
    {
        string text = output.ReplaceLineEndings("\n");
        if (text.EndsWith('\n'))
        {
            text = text[..^1];
        }

        return [.. text.Split('\n').Select(Escape)];
    }

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
