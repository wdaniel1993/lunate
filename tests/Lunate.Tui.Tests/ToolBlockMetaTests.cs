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

    [Fact]
    public void Read_compact_meta_carries_the_result_line_count()
    {
        var block = new ToolBlockModel(
            "read",
            """{"path":"src/x.cs"}""",
            ToolBlockStatus.Ok,
            string.Join('\n', Enumerable.Range(1, 13).Select(n => $"line {n}")),
            null
        );

        Assert.Equal("13 lines", ToolBlockMeta.Build(block, compact: true));
    }

    [Fact]
    public void An_image_read_has_no_compact_line_meta()
    {
        var block = new ToolBlockModel(
            "read",
            """{"path":"screens/diagram.png"}""",
            ToolBlockStatus.Ok,
            "[image 1024x768]",
            null
        )
        {
            IsImage = true,
        };

        Assert.Null(ToolBlockMeta.Build(block, compact: true));
    }

    [Fact]
    public void A_failed_read_has_no_compact_meta()
    {
        var block = new ToolBlockModel(
            "read",
            """{"path":"src/missing.cs"}""",
            ToolBlockStatus.Error,
            "file not found: src/missing.cs",
            null
        );

        Assert.Null(ToolBlockMeta.Build(block, compact: true));
    }

    [Fact]
    public void Cs_diagnostics_meta_carries_the_counts_and_the_top_items()
    {
        var block = CsBlock(DiagnosticsOutput);

        Assert.Equal(
            "2 errors 1 warning"
                + " \u00b7 src/a.cs(4,5): error CS0103: The name 'Foo' does not exist"
                + " \u00b7 src/b.cs(9,1): warning CS8600: Converting null literal",
            ToolBlockMeta.Build(block)
        );
    }

    [Fact]
    public void Cs_diagnostics_meta_caps_the_item_list()
    {
        string? meta = ToolBlockMeta.Build(CsBlock(DiagnosticsOutput));

        Assert.DoesNotContain("CS1002", meta, StringComparison.Ordinal);
    }

    [Fact]
    public void Cs_diagnostics_compact_meta_is_the_severity_counts_only() =>
        Assert.Equal(
            "2 errors 1 warning",
            ToolBlockMeta.Build(CsBlock(DiagnosticsOutput), compact: true)
        );

    [Fact]
    public void Cs_diagnostics_meta_truncates_a_long_item()
    {
        string message = new('q', 200);
        var block = CsBlock(
            "1 error, 0 warnings in the solution.\nsrc/a.cs(1,1): error CS9999: " + message
        );

        string? meta = ToolBlockMeta.Build(block);

        Assert.NotNull(meta);
        Assert.EndsWith("\u2026", meta);
        Assert.DoesNotContain(new string('q', 100), meta, StringComparison.Ordinal);
    }

    [Fact]
    public void Ascii_meta_joins_with_ascii_punctuation()
    {
        var bash = new ToolBlockModel(
            "bash",
            """{"command":"dotnet build"}""",
            ToolBlockStatus.Ok,
            "Build succeeded.",
            null,
            ExitCode: 0,
            Duration: TimeSpan.FromMilliseconds(400)
        );
        var edit = new ToolBlockModel(
            "edit",
            """{"path":"src/x.cs"}""",
            ToolBlockStatus.Ok,
            null,
            new ToolDiffInfo("src/x.cs", "exact", DiffText)
        );

        Assert.Equal("exit 0 - 0.4s", ToolBlockMeta.Build(bash, unicode: false));
        Assert.Equal("+1 -1", ToolBlockMeta.Build(edit, unicode: false));
    }

    [Fact]
    public void Ascii_cs_items_truncate_with_three_dots()
    {
        string message = new('q', 200);
        string? meta = ToolBlockMeta.Build(
            CsBlock(
                "1 error, 0 warnings in the solution.\nsrc/a.cs(1,1): error CS9999: " + message
            ),
            unicode: false
        );

        Assert.NotNull(meta);
        Assert.EndsWith("...", meta);
        Assert.DoesNotContain("\u2026", meta, StringComparison.Ordinal);
    }

    [Fact]
    public void Cs_diagnostics_with_no_findings_has_no_meta() =>
        Assert.Null(
            ToolBlockMeta.Build(CsBlock("No compiler errors or warnings in the solution."))
        );

    [Fact]
    public void Cs_diagnostics_with_no_findings_has_no_compact_meta() =>
        Assert.Null(
            ToolBlockMeta.Build(
                CsBlock("No compiler errors or warnings in the solution."),
                compact: true
            )
        );

    [Fact]
    public void A_cs_result_without_a_summary_line_has_no_meta() =>
        Assert.Null(ToolBlockMeta.Build(CsBlock("Symbols:\nWidget.Method — src/a.cs")));

    [Fact]
    public void Cs_diagnostics_reads_the_counts_past_a_partial_load_notice() =>
        Assert.StartsWith(
            "1 error 0 warnings",
            ToolBlockMeta.Build(
                CsBlock(
                    "Partial load: one project failed.\n"
                        + "1 error, 0 warnings in changed files.\n"
                        + "src/a.cs(4,5): error CS0103: The name 'Foo' does not exist"
                )
            ),
            StringComparison.Ordinal
        );

    private static ToolBlockModel CsBlock(string output) =>
        new("cs_diagnostics", """{"scope":"changed"}""", ToolBlockStatus.Error, output, null);

    private const string DiagnosticsOutput =
        "Partial load: one project failed to load.\n"
        + "2 errors, 1 warning in changed files.\n"
        + "src/a.cs(4,5): error CS0103: The name 'Foo' does not exist\n"
        + "src/b.cs(9,1): warning CS8600: Converting null literal\n"
        + "src/c.cs(1,1): error CS1002: ; expected";

    private const string DiffText =
        "--- a/src/x.cs\n"
        + "+++ b/src/x.cs\n"
        + "@@ -1,3 +1,3 @@\n"
        + " int a = 1;\n"
        + "-int b = 2;\n"
        + "+int b = 3;\n"
        + " int c = 4;";
}
