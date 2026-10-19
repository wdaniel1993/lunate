namespace Lunate.Tui.Tests;

public sealed class ToolBlockMetaTests
{
    [Fact]
    public void Bash_meta_joins_the_exit_code_and_duration()
    {
        var block = new ToolBlockModel(
            "bash",
            """{"command":"dotnet build"}""",
            ToolBlockStatus.Ok,
            "Build succeeded.",
            null,
            ExitCode: 0,
            Duration: TimeSpan.FromMilliseconds(400)
        );

        Assert.Equal("exit 0 \u00b7 0.4s", ToolBlockMeta.Build(block));
    }

    [Fact]
    public void Bash_error_meta_carries_the_exit_code_without_a_duration()
    {
        var block = new ToolBlockModel(
            "bash",
            """{"command":"dotnet build"}""",
            ToolBlockStatus.Error,
            "error CS0103",
            null,
            ExitCode: 1
        );

        Assert.Equal("exit 1", ToolBlockMeta.Build(block));
    }

    [Fact]
    public void Bash_meta_is_empty_without_an_exit_code_or_duration()
    {
        var block = new ToolBlockModel(
            "bash",
            """{"command":"ls"}""",
            ToolBlockStatus.Ok,
            "out",
            null
        );

        Assert.Null(ToolBlockMeta.Build(block));
    }

    [Fact]
    public void Edit_meta_counts_the_diff_insertions_and_deletions()
    {
        var block = new ToolBlockModel(
            "edit",
            """{"path":"src/x.cs"}""",
            ToolBlockStatus.Ok,
            null,
            new ToolDiffInfo("src/x.cs", "exact", DiffText)
        );

        Assert.Equal("+1 \u22121", ToolBlockMeta.Build(block));
    }

    [Fact]
    public void Write_meta_counts_a_pure_insertion()
    {
        var block = new ToolBlockModel(
            "write",
            """{"path":"src/new.cs"}""",
            ToolBlockStatus.Ok,
            null,
            new ToolDiffInfo(
                "src/new.cs",
                "created",
                "--- a/src/new.cs\n+++ b/src/new.cs\n@@ -0,0 +1,3 @@\n+namespace App;\n+\n+class New { }"
            )
        );

        Assert.Equal("+3 \u22120", ToolBlockMeta.Build(block));
    }

    [Fact]
    public void An_edit_without_a_diff_has_no_meta()
    {
        var block = new ToolBlockModel(
            "edit",
            """{"path":"src/x.cs"}""",
            ToolBlockStatus.Ok,
            null,
            null
        );

        Assert.Null(ToolBlockMeta.Build(block));
    }

    [Fact]
    public void Read_has_no_meta() =>
        Assert.Null(
            ToolBlockMeta.Build(
                new ToolBlockModel(
                    "read",
                    """{"path":"src/x.cs"}""",
                    ToolBlockStatus.Ok,
                    "content",
                    null,
                    null,
                    TimeSpan.FromSeconds(2)
                )
            )
        );

    private const string DiffText =
        "--- a/src/x.cs\n"
        + "+++ b/src/x.cs\n"
        + "@@ -1,3 +1,3 @@\n"
        + " int a = 1;\n"
        + "-int b = 2;\n"
        + "+int b = 3;\n"
        + " int c = 4;";
}
