using Spectre.Console;
using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

/// <summary>
/// The golden frame for hostile tool output (#447): a result carrying OSC 52 (clipboard write),
/// OSC 8 (a link), C1 CSI and C0 controls commits to the scrollback with every control byte
/// dropped and the printable text intact, so no byte the TUI did not write reaches the terminal.
/// Regenerate deliberately with
/// <c>LUNATE_TUI_UPDATE_GOLDENS=1 dotnet test --project tests/Lunate.Tui.Tests</c>.
/// </summary>
public sealed class EscapeInjectionGoldenTests
{
    private const string HostileOutput =
        "\u001b]52;c;Q2xpY2JvYXJk\u0007"
        + "clipboard owned\n"
        + "\u001b]8;;https://evil.example\u0007click\u001b]8;;\u0007fake link\n"
        + "\u009b2J\u009b31mred screen\u0004plain again";

    private static readonly string[] Payloads = ["\u001b]52", "\u001b]8", "\u009b", "\u0004"];

    [Fact]
    public void The_hostile_tool_block_frame_matches_the_committed_golden()
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.EmitAnsiSequences = true;
        console.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
        console.Write(
            new ToolBlockRenderer(
                new TerminalCapabilities(ColorSystemSupport.NoColors, Unicode: true),
                homeDirectory: "/home/dev"
            ).Render(Block(), 80, ToolDetails.Expanded)
        );

        string output = console.Output.ReplaceLineEndings("\n");
        foreach (string payload in Payloads)
        {
            Assert.DoesNotContain(payload, output, StringComparison.Ordinal);
        }

        // The readable text survives the scrub; only the sequences went.
        Assert.Contains("clipboard owned", output);
        Assert.Contains("fake link", output);
        Assert.Contains("red screen", output);
        Assert.Contains("plain again", output);

        GoldenFiles.AssertMatchesText(
            Path.Combine(GoldensDirectory, "hostile-toolblock.txt"),
            output
        );
    }

    private static ToolBlockModel Block() =>
        new(
            "bash",
            """{"command":"echo hostile"}""",
            ToolBlockStatus.Ok,
            HostileOutput,
            null,
            ExitCode: 0,
            Duration: TimeSpan.FromMilliseconds(120)
        );

    private static string GoldensDirectory =>
        Path.Combine(
            TestPaths.RepositoryRoot,
            "tests",
            "Lunate.Tui.Tests",
            "fixtures",
            "escape-injection"
        );
}
