using Spectre.Console;
using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

/// <summary>
/// The welcome header's committed forms (design D2/D4): what the terminal shows for a composing
/// session, a narrow terminal and a first run. Regenerate deliberately with
/// <c>LUNATE_TUI_UPDATE_GOLDENS=1 dotnet test --project tests/Lunate.Tui.Tests</c>.
/// </summary>
public sealed class WelcomeHeaderGoldenTests
{
    private static string GoldensDirectory =>
        Path.Combine(
            TestPaths.RepositoryRoot,
            "tests",
            "Lunate.Tui.Tests",
            "fixtures",
            "welcome-header"
        );

    private static readonly WelcomeHeaderState Composing = new()
    {
        Version = "0.1.0",
        ModelId = "deepseek-v4.1-flash",
        ContextWindow = 1_000_000,
        Workspace = "~/dev/lunate",
        CSharpWorkspace = true,
    };

    [Fact]
    public void The_full_block_matches_the_committed_golden() =>
        GoldenFiles.AssertMatchesText(
            Path.Combine(GoldensDirectory, "full-block.txt"),
            Render(
                Composing,
                width: 80,
                new TerminalCapabilities(ColorSystemSupport.TrueColor, Unicode: true)
            )
        );

    [Fact]
    public void The_single_line_form_matches_the_committed_golden() =>
        GoldenFiles.AssertMatchesText(
            Path.Combine(GoldensDirectory, "single-line.txt"),
            Render(
                Composing,
                width: 40,
                new TerminalCapabilities(ColorSystemSupport.TrueColor, Unicode: true)
            )
        );

    [Fact]
    public void The_single_line_ascii_form_matches_the_committed_golden() =>
        GoldenFiles.AssertMatchesText(
            Path.Combine(GoldensDirectory, "single-line-ascii.txt"),
            Render(
                Composing,
                width: 40,
                new TerminalCapabilities(ColorSystemSupport.TrueColor, Unicode: false)
            )
        );

    [Fact]
    public void The_first_run_form_matches_the_committed_golden() =>
        GoldenFiles.AssertMatchesText(
            Path.Combine(GoldensDirectory, "first-run.txt"),
            Render(
                Composing with
                {
                    Fix = "add the 'openai' key to ~/.lunate/auth.json",
                },
                width: 80,
                new TerminalCapabilities(ColorSystemSupport.TrueColor, Unicode: true)
            )
        );

    private static string Render(
        WelcomeHeaderState state,
        int width,
        TerminalCapabilities capabilities
    )
    {
        var console = new TestConsole();
        console.Profile.Width = 200;
        foreach (string line in WelcomeHeaderRenderer.Render(state, capabilities, width))
        {
            console.Write(new Markup(line + "\n"));
        }

        return console.Output;
    }
}
