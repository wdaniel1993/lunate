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
                "edit-diff-exact",
                new ToolBlockModel(
                    "edit",
                    """{"path":"src/Counter.cs","old_text":"x","new_text":"y"}""",
                    ToolBlockStatus.Ok,
                    "edited src/Counter.cs lines 4\u20135 (match: exact)",
                    new ToolDiffInfo(
                        "src/Counter.cs",
                        "exact",
                        "--- a/src/Counter.cs\n"
                            + "+++ b/src/Counter.cs\n"
                            + "@@ -2,4 +2,4 @@\n"
                            + " public void Increment()\n"
                            + " {\n"
                            + "-    _count = _count + 1;\n"
                            + "+    _count++;\n"
                            + " }"
                    )
                )
            },
            {
                "edit-diff-normalized",
                new ToolBlockModel(
                    "edit",
                    """{"path":"src/Counter.cs","old_text":"x  ","new_text":"y"}""",
                    ToolBlockStatus.Ok,
                    "edited src/Counter.cs lines 4\u20134 (match: normalized)",
                    new ToolDiffInfo(
                        "src/Counter.cs",
                        "normalized",
                        "--- a/src/Counter.cs\n"
                            + "+++ b/src/Counter.cs\n"
                            + "@@ -3,3 +3,3 @@\n"
                            + " {\n"
                            + "-    _count = _count + 1;  \n"
                            + "+    _count++;\n"
                            + " }"
                    )
                )
            },
            {
                "write-diff",
                new ToolBlockModel(
                    "write",
                    """{"path":"src/NewFile.cs","content":"namespace App;"}""",
                    ToolBlockStatus.Ok,
                    "wrote 3 lines to src/NewFile.cs (created)",
                    new ToolDiffInfo(
                        "src/NewFile.cs",
                        "exact",
                        "--- a/src/NewFile.cs\n"
                            + "+++ b/src/NewFile.cs\n"
                            + "@@ -0,0 +1,3 @@\n"
                            + "+namespace App;\n"
                            + "+\n"
                            + "+internal sealed class NewFile { }"
                    )
                )
            },
            {
                "escaping",
                new ToolBlockModel(
                    "edit",
                    """{"path":"[red]src[/].cs"}""",
                    ToolBlockStatus.Ok,
                    "[dim]not markup[/]\n[bold]neither[/]\n[[nested]]\n",
                    new ToolDiffInfo(
                        "[red]src[/].cs",
                        "[bold]exact[/]",
                        "--- a/[red]src[/].cs\n"
                            + "+++ b/[red]src[/].cs\n"
                            + "@@ -1 +1 @@\n"
                            + "-keep [dim]this[/]\n"
                            + "+keep [bold]that[/]"
                    )
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
