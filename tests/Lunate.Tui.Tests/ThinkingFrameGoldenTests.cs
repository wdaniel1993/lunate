using Spectre.Console;
using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

/// <summary>
/// The thinking states' committed snapshots (visual-spec §3.3, tui spec "Thinking display"): the
/// live block, the collapsed commit and the full-text commit, at 120/80/50 columns and in every
/// colour mode the capability helper selects (truecolor / 16-colour / NO_COLOR). Regenerate
/// deliberately with
/// <c>LUNATE_TUI_UPDATE_GOLDENS=1 dotnet test --project tests/Lunate.Tui.Tests</c>.
/// </summary>
public sealed class ThinkingFrameGoldenTests
{
    private static string GoldensDirectory =>
        Path.Combine(
            TestPaths.RepositoryRoot,
            "tests",
            "Lunate.Tui.Tests",
            "fixtures",
            "frames",
            "thinking"
        );

    private const string ReasoningText =
        "We need to add validation to TodoList.Add. Null, empty and whitespace-only titles must "
        + "throw ArgumentException. The check belongs at the top of the method, before the list mutates.";

    private static readonly StatusFooterModel Footer = new(
        "deepseek-v4.1-flash",
        2100,
        1_000_000,
        "~/dev/lunate",
        "main"
    )
    {
        ThinkLevel = "high",
    };

    public static TheoryData<int, string> Cases =>
        new()
        {
            { 120, "truecolor" },
            { 120, "256-color" },
            { 120, "16-color" },
            { 120, "no-color" },
            { 80, "truecolor" },
            { 80, "256-color" },
            { 80, "16-color" },
            { 80, "no-color" },
            { 50, "truecolor" },
            { 50, "256-color" },
            { 50, "16-color" },
            { 50, "no-color" },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_live_thinking_frame_matches_the_committed_golden(int width, string mode)
    {
        var state = new LiveAreaState
        {
            Thinking = new ThinkingState { Text = ReasoningText, ElapsedSeconds = 6 },
            Footer = Footer,
            Color = Capability(mode),
            Size = new ConsoleSize(width, 24),
        };

        GoldenFiles.AssertMatchesLines(
            Path.Combine(GoldensDirectory, $"live-{width}-{mode}.txt"),
            LiveAreaRenderer.Render(state).Lines.Select(Escape).ToArray()
        );
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_collapsed_commit_matches_the_committed_golden(int width, string mode)
    {
        var commit = new ThinkingCommitModel(ReasoningText, 12, Full: false);

        GoldenFiles.AssertMatchesLines(
            Path.Combine(GoldensDirectory, $"collapsed-{width}-{mode}.txt"),
            RenderCommit(commit, width, mode)
        );
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_full_commit_matches_the_committed_golden(int width, string mode)
    {
        var commit = new ThinkingCommitModel(ReasoningText, 12, Full: true);

        GoldenFiles.AssertMatchesLines(
            Path.Combine(GoldensDirectory, $"full-{width}-{mode}.txt"),
            RenderCommit(commit, width, mode)
        );
    }

    /// <summary>The capability from the detection seam: Detect is the 16-colour bucket.</summary>
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

    private static string[] RenderCommit(ThinkingCommitModel commit, int width, string mode)
    {
        var console = new TestConsole();
        console.Profile.Width = 200;
        console.EmitAnsiSequences = true;
        console.Profile.Capabilities.ColorSystem = ConsoleColor(mode);
        console.Write(new ThinkingCommitRenderer().Render(commit, width, Capability(mode)));
        return console
            .Output.ReplaceLineEndings("\n")
            .TrimEnd('\n')
            .Split('\n')
            .Select(Escape)
            .ToArray();
    }

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\")
            .Replace("\u001b", "\\x1b")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
}
