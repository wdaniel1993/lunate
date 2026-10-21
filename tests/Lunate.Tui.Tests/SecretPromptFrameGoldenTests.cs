using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// The masked secret prompt's committed frame: one clipped line after the notice and above the
/// upper rule, at 80x24 without colour. Regenerate deliberately with
/// <c>LUNATE_TUI_UPDATE_GOLDENS=1 dotnet test tests/Lunate.Tui.Tests/Lunate.Tui.Tests.csproj --filter "FullyQualifiedName~SecretPromptFrameGoldenTests"</c>.
/// </summary>
public sealed class SecretPromptFrameGoldenTests
{
    private static string GoldensDirectory =>
        Path.Combine(TestPaths.RepositoryRoot, "tests", "Lunate.Tui.Tests", "fixtures", "frames");

    [Fact]
    public void The_secret_prompt_frame_matches_the_committed_golden()
    {
        var state = new LiveAreaState
        {
            Notice = "sign in to the provider",
            SecretPrompt = new SecretPromptState("API key for groq", "*************"),
            Footer = new StatusFooterModel(
                "deepseek-v4.1-flash",
                2100,
                1_000_000,
                "~/dev/lunate",
                "main"
            ),
            Color = ColorSystemSupport.NoColors,
            Unicode = true,
            Size = new ConsoleSize(80, 24),
        };

        string[] lines = [.. LiveAreaRenderer.Render(state).Lines];

        GoldenFiles.AssertMatchesLines(Path.Combine(GoldensDirectory, "secret-prompt.txt"), lines);

        int notice = Array.IndexOf(lines, "sign in to the provider");
        int prompt = Array.IndexOf(lines, "API key for groq: " + new string('*', 13));
        int rule = Array.IndexOf(lines, new string('─', 80));
        Assert.True(notice >= 0, "the notice renders");
        Assert.True(prompt > notice, "the secret prompt renders after the notice");
        Assert.True(rule > prompt, "the secret prompt renders above the upper rule");
    }

    [Fact]
    public void A_narrow_frame_clips_the_prompt_to_one_line()
    {
        var state = new LiveAreaState
        {
            SecretPrompt = new SecretPromptState("API key for groq", new string('*', 13)),
            Color = ColorSystemSupport.NoColors,
            Unicode = true,
            Size = new ConsoleSize(20, 12),
        };

        string[] lines = [.. LiveAreaRenderer.Render(state).Lines];

        Assert.Contains("API key for groq: **", lines);
        Assert.Equal(
            1,
            lines.Count(line => line.StartsWith("API key for groq", StringComparison.Ordinal))
        );
    }
}
