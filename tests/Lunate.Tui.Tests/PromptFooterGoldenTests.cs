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
}
