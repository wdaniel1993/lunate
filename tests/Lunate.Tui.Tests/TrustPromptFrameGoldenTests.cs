using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// The trust prompt's committed frames at 80 and 118 columns: a realistic extension path is long
/// enough to need a wrapped title, so every path segment and the choices must stay visible.
/// Regenerate deliberately with
/// <c>LUNATE_TUI_UPDATE_GOLDENS=1 dotnet test tests/Lunate.Tui.Tests/Lunate.Tui.Tests.csproj --filter "FullyQualifiedName~TrustPromptFrameGoldenTests"</c>.
/// </summary>
public sealed class TrustPromptFrameGoldenTests
{
    private static readonly TrustPromptModel Prompt = new(
        "Load project extension 'hello-world' 0.1.0 from /Users/danielwallner/dev/lunate/demo/extensions/hello-world",
        [],
        0,
        0
    );

    private static string GoldensDirectory =>
        Path.Combine(TestPaths.RepositoryRoot, "tests", "Lunate.Tui.Tests", "fixtures", "trust");

    [Theory]
    [InlineData(80)]
    [InlineData(118)]
    public void The_trust_prompt_frame_keeps_the_choices_visible(int width)
    {
        var state = new LiveAreaState
        {
            Trust = Prompt,
            Color = ColorSystemSupport.NoColors,
            Unicode = true,
            Size = new ConsoleSize(width, 24),
        };

        string[] lines = [.. LiveAreaRenderer.Render(state).Lines];

        GoldenFiles.AssertMatchesLines(Path.Combine(GoldensDirectory, $"trust-{width}.txt"), lines);

        Assert.All(
            lines,
            line =>
                Assert.True(
                    CellText.Width(line) <= width,
                    $"{CellText.Width(line)} cells exceed {width}: {line}"
                )
        );
        string headline = Assert.Single(
            lines,
            line => line.Contains("[y]es", StringComparison.Ordinal)
        );
        Assert.EndsWith("[y]es  [n]o", headline, StringComparison.Ordinal);
    }
}
