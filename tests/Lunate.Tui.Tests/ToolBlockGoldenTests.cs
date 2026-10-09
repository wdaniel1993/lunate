using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

public sealed class ToolBlockGoldenTests
{
    private static string GoldensDirectory =>
        Path.Combine(
            TestPaths.RepositoryRoot,
            "tests",
            "Lunate.Tui.Tests",
            "fixtures",
            "toolblocks"
        );

    public static TheoryData<string, ToolBlockModel> Blocks =>
        new()
        {
            {
                "read-ok",
                new ToolBlockModel(
                    "read",
                    """{"path":"src/Lunate.Tui/FrameWriter.cs"}""",
                    ToolBlockStatus.Ok,
                    """
                    namespace Lunate.Tui;

                    internal sealed class FrameWriter
                    {
                        public void Write(string text) { }
                    }
                    """,
                    null
                )
            },
            {
                "bash-ok",
                new ToolBlockModel(
                    "bash",
                    """{"command":"dotnet test --project tests/Lunate.Tui.Tests"}""",
                    ToolBlockStatus.Ok,
                    """
                    Test run summary: Passed!
                      total: 286
                      failed: 0
                      succeeded: 286
                    """,
                    null
                )
            },
            {
                "error",
                new ToolBlockModel(
                    "read",
                    """{"path":"src/missing.cs"}""",
                    ToolBlockStatus.Error,
                    "file not found: src/missing.cs",
                    null
                )
            },
            {
                "long-output",
                new ToolBlockModel(
                    "bash",
                    """{"command":"dotnet build"}""",
                    ToolBlockStatus.Ok,
                    string.Join('\n', Enumerable.Range(1, 30).Select(n => $"step {n:00} finished")),
                    null
                )
            },
            {
                "running",
                new ToolBlockModel(
                    "bash",
                    """{"command":"dotnet test"}""",
                    ToolBlockStatus.Running,
                    null,
                    null
                )
            },
            {
                "no-output",
                new ToolBlockModel("read", """{"path":"README.md"}""", ToolBlockStatus.Ok, "", null)
            },
            {
                "escaping",
                new ToolBlockModel(
                    "edit",
                    """{"path":"[red]src[/].cs"}""",
                    ToolBlockStatus.Ok,
                    "[dim]not markup[/]\n[bold]neither[/]\n[[nested]]\n",
                    null
                )
            },
        };

    [Theory]
    [MemberData(nameof(Blocks))]
    public void Fixture_matches_the_committed_golden(string name, ToolBlockModel block)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.Write(new ToolBlockRenderer().Render(block));

        GoldenFiles.AssertMatchesText(
            Path.Combine(GoldensDirectory, name + ".txt"),
            console.Output
        );
    }
}
