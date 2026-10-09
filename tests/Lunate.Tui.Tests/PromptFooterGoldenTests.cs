using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

public sealed class PromptFooterGoldenTests
{
    private static string GoldensDirectory =>
        Path.Combine(
            TestPaths.RepositoryRoot,
            "tests",
            "Lunate.Tui.Tests",
            "fixtures",
            "prompt-footer"
        );

    private static readonly StatusFooterModel Footer = new(
        "deepseek-v4.1-flash",
        12400,
        128000,
        "~/dev/lunate",
        "main"
    );

    public static TheoryData<string, int> FooterGoldens =>
        new()
        {
            { "footer-full", 80 },
            { "footer-narrow", 60 },
            { "footer-minimal", 50 },
        };

    [Fact]
    public void Approval_prompt_matches_the_committed_golden()
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.Write(
            new ApprovalPromptRenderer().Render(
                new ApprovalPromptModel("bash", """{"command":"dotnet format"}""")
            )
        );

        GoldenFiles.AssertMatchesText(
            Path.Combine(GoldensDirectory, "approval-prompt.txt"),
            console.Output
        );
    }

    [Theory]
    [MemberData(nameof(FooterGoldens))]
    public void Footer_matches_the_committed_golden(string name, int width)
    {
        var console = new TestConsole();
        console.Profile.Width = 200;
        console.Write(new StatusFooterRenderer().Render(Footer, width));

        GoldenFiles.AssertMatchesText(
            Path.Combine(GoldensDirectory, name + ".txt"),
            console.Output
        );
    }
}
