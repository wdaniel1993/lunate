using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

public sealed class ToolBlockRendererTests
{
    [Fact]
    public void Null_model_is_rejected() =>
        Assert.Throws<ArgumentNullException>(() => new ToolBlockRenderer().Render(null!));

    [Fact]
    public void Header_carries_tool_name_args_summary_and_status() =>
        Assert.Equal(
            "tool read src/a.cs ok\n",
            Render(
                new ToolBlockModel(
                    "read",
                    """{"path":"src/a.cs"}""",
                    ToolBlockStatus.Ok,
                    null,
                    null
                )
            )
        );

    [Fact]
    public void Pre_summarized_args_stay_as_they_are() =>
        Assert.Equal(
            "tool read src/a.cs ok\n",
            Render(new ToolBlockModel("read", "src/a.cs", ToolBlockStatus.Ok, null, null))
        );

    [Fact]
    public void Output_lines_render_below_the_header() =>
        Assert.Equal(
            "tool bash ls ok\none\ntwo\n",
            Render(
                new ToolBlockModel(
                    "bash",
                    """{"command":"ls"}""",
                    ToolBlockStatus.Ok,
                    "one\ntwo\n",
                    null
                )
            )
        );

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Blank_output_renders_only_the_header(string? output) =>
        Assert.Equal(
            "tool bash ls ok\n",
            Render(
                new ToolBlockModel("bash", """{"command":"ls"}""", ToolBlockStatus.Ok, output, null)
            )
        );

    [Fact]
    public void Ok_status_uses_green_ansi() =>
        Assert.Contains(
            "\u001b[38;5;2mok",
            RenderAnsi(new ToolBlockModel("read", "src/a.cs", ToolBlockStatus.Ok, null, null))
        );

    [Fact]
    public void Failed_status_uses_red_ansi() =>
        Assert.Contains(
            "\u001b[38;5;9mfailed",
            RenderAnsi(new ToolBlockModel("read", "src/a.cs", ToolBlockStatus.Error, null, null))
        );

    [Fact]
    public void Running_status_uses_dim_ansi() =>
        Assert.Contains(
            "\u001b[2mrunning",
            RenderAnsi(
                new ToolBlockModel(
                    "bash",
                    """{"command":"ls"}""",
                    ToolBlockStatus.Running,
                    null,
                    null
                )
            )
        );

    [Fact]
    public void Elision_marker_uses_dim_ansi()
    {
        string output = string.Join('\n', Enumerable.Range(1, 18).Select(n => $"line {n}"));

        string ansi = RenderAnsi(
            new ToolBlockModel("bash", """{"command":"ls"}""", ToolBlockStatus.Ok, output, null)
        );

        Assert.Contains("\u001b[2m\u2026 2 lines hidden \u2026", ansi);
    }

    [Fact]
    public void Escaping_keeps_bracket_text_literal()
    {
        string output = Render(
            new ToolBlockModel(
                "edit",
                """{"path":"[red]src[/].cs"}""",
                ToolBlockStatus.Ok,
                "[dim]not markup[/]\n[bold]neither[/]\n[[nested]]\n",
                new ToolDiffInfo(
                    "[red]src[/].cs",
                    "[bold]exact[/]",
                    "--- a/[red]src[/].cs\n+++ b/[red]src[/].cs\n@@ -1 +1 @@\n-keep [dim]this[/]\n+keep [bold]that[/]"
                )
            )
        );

        Assert.Equal(
            "tool edit [red]src[/].cs ok\n"
                + "[dim]not markup[/]\n"
                + "[bold]neither[/]\n"
                + "[[nested]]\n"
                + "match: [bold]exact[/]\n"
                + "--- a/[red]src[/].cs\n"
                + "+++ b/[red]src[/].cs\n"
                + "@@ -1 +1 @@\n"
                + "-keep [dim]this[/]\n"
                + "+keep [bold]that[/]\n",
            output
        );
    }

    [Fact]
    public void Diff_panel_renders_below_header_and_output() =>
        Assert.Equal(
            "tool edit src/x.cs ok\n"
                + "edited src/x.cs lines 2\u20132 (match: exact)\n"
                + "match: exact\n"
                + "--- a/src/x.cs\n"
                + "+++ b/src/x.cs\n"
                + "@@ -1,3 +1,3 @@\n"
                + " int a = 1;\n"
                + "-int b = 2;\n"
                + "+int b = 3;\n"
                + " int c = 4;\n",
            Render(
                new ToolBlockModel(
                    "edit",
                    """{"path":"src/x.cs"}""",
                    ToolBlockStatus.Ok,
                    "edited src/x.cs lines 2\u20132 (match: exact)",
                    new ToolDiffInfo("src/x.cs", "exact", DiffText)
                )
            )
        );

    [Fact]
    public void Exact_tier_label_is_dim() =>
        Assert.Contains("\u001b[2mmatch: exact", RenderAnsi(DiffModel("exact")));

    [Fact]
    public void Normalized_tier_label_is_yellow() =>
        Assert.Contains("\u001b[38;5;11mmatch: normalized", RenderAnsi(DiffModel("normalized")));

    [Fact]
    public void Indent_tier_label_is_yellow() =>
        Assert.Contains("\u001b[38;5;11mmatch: indent", RenderAnsi(DiffModel("indent")));

    [Fact]
    public void Insertions_are_green_and_deletions_are_red()
    {
        string ansi = RenderAnsi(DiffModel("exact"));

        Assert.Contains("\u001b[38;5;2m+int b = 3;", ansi);
        Assert.Contains("\u001b[38;5;9m-int b = 2;", ansi);
    }

    [Fact]
    public void File_headers_are_dim_and_hunks_are_dim_cyan()
    {
        string ansi = RenderAnsi(DiffModel("exact"));

        Assert.Contains("\u001b[2m--- a/src/x.cs", ansi);
        Assert.Contains("\u001b[2;38;5;14m@@ -1,3 +1,3 @@", ansi);
    }

    [Fact]
    public void Empty_diff_text_omits_the_panel() =>
        Assert.Equal(
            "tool edit src/x.cs ok\n",
            Render(
                new ToolBlockModel(
                    "edit",
                    """{"path":"src/x.cs"}""",
                    ToolBlockStatus.Ok,
                    null,
                    new ToolDiffInfo("src/x.cs", "exact", "")
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

    private static ToolBlockModel DiffModel(string tier) =>
        new(
            "edit",
            """{"path":"src/x.cs"}""",
            ToolBlockStatus.Ok,
            null,
            new ToolDiffInfo("src/x.cs", tier, DiffText)
        );

    private static string Render(ToolBlockModel block)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.Write(new ToolBlockRenderer().Render(block));
        return console.Output;
    }

    private static string RenderAnsi(ToolBlockModel block)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.EmitAnsiSequences = true;
        console.Write(new ToolBlockRenderer().Render(block));
        return console.Output;
    }
}
