using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

public sealed class ToolBlockRendererTests
{
    [Fact]
    public void Null_model_is_rejected() =>
        Assert.Throws<ArgumentNullException>(() => new ToolBlockRenderer().Render(null!));

    [Fact]
    public void Header_leads_with_the_ok_glyph_name_and_summary() =>
        Assert.Equal(
            "\u2713 read  src/a.cs\n",
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
    public void A_failed_call_leads_with_the_cross_glyph() =>
        Assert.Equal(
            "\u2717 bash  ls\n",
            Render(
                new ToolBlockModel(
                    "bash",
                    """{"command":"ls"}""",
                    ToolBlockStatus.Error,
                    null,
                    null
                )
            )
        );

    [Fact]
    public void The_header_carries_the_tool_meta() =>
        Assert.Equal(
            "\u2713 bash  dotnet build \u00b7 exit 0 \u00b7 0.4s\n",
            Render(
                new ToolBlockModel(
                    "bash",
                    """{"command":"dotnet build"}""",
                    ToolBlockStatus.Ok,
                    null,
                    null,
                    ExitCode: 0,
                    Duration: TimeSpan.FromMilliseconds(400)
                )
            )
        );

    [Fact]
    public void No_status_words_remain()
    {
        string ok = Render(
            new ToolBlockModel("read", """{"path":"src/a.cs"}""", ToolBlockStatus.Ok, null, null)
        );
        string failed = Render(
            new ToolBlockModel(
                "read",
                """{"path":"src/a.cs"}""",
                ToolBlockStatus.Error,
                null,
                null
            )
        );
        string running = Render(
            new ToolBlockModel(
                "bash",
                """{"command":"ls"}""",
                ToolBlockStatus.Running,
                null,
                null
            )
        );

        Assert.DoesNotContain("ok", ok, StringComparison.Ordinal);
        Assert.DoesNotContain("failed", failed, StringComparison.Ordinal);
        Assert.DoesNotContain("running", running, StringComparison.Ordinal);
    }

    [Fact]
    public void Ascii_capabilities_use_the_ascii_glyphs()
    {
        string ok = RenderAscii(
            new ToolBlockModel("read", """{"path":"src/a.cs"}""", ToolBlockStatus.Ok, null, null)
        );
        string failed = RenderAscii(
            new ToolBlockModel(
                "read",
                """{"path":"src/a.cs"}""",
                ToolBlockStatus.Error,
                null,
                null
            )
        );

        Assert.Equal("+ read  src/a.cs\n", ok);
        Assert.Equal("x read  src/a.cs\n", failed);
    }

    [Fact]
    public void Pre_summarized_args_stay_as_they_are() =>
        Assert.Equal(
            "\u2713 read  src/a.cs\n",
            Render(new ToolBlockModel("read", "src/a.cs", ToolBlockStatus.Ok, null, null))
        );

    [Fact]
    public void Output_lines_render_below_the_header() =>
        Assert.Equal(
            "\u2713 bash  ls\none\ntwo\n",
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
            "\u2713 bash  ls\n",
            Render(
                new ToolBlockModel("bash", """{"command":"ls"}""", ToolBlockStatus.Ok, output, null)
            )
        );

    [Fact]
    public void The_ok_glyph_uses_green_ansi() =>
        Assert.Contains(
            "\u001b[38;5;2m\u2713",
            RenderAnsi(new ToolBlockModel("read", "src/a.cs", ToolBlockStatus.Ok, null, null))
        );

    [Fact]
    public void The_failed_glyph_uses_red_ansi() =>
        Assert.Contains(
            "\u001b[38;5;9m\u2717",
            RenderAnsi(new ToolBlockModel("read", "src/a.cs", ToolBlockStatus.Error, null, null))
        );

    [Fact]
    public void The_running_glyph_uses_dim_ansi() =>
        Assert.Contains(
            "\u001b[2m\u2026",
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
    public void The_name_is_accent_and_the_summary_is_muted()
    {
        string ansi = RenderAnsi(
            new ToolBlockModel(
                "bash",
                """{"command":"dotnet build"}""",
                ToolBlockStatus.Ok,
                null,
                null
            )
        );

        Assert.Contains("\u001b[38;5;14mbash", ansi);
        Assert.Contains("\u001b[2m  dotnet build", ansi);
    }

    [Fact]
    public void The_meta_is_muted() =>
        Assert.Contains(
            "\u001b[2m \u00b7 exit 0 \u00b7 0.4s",
            RenderAnsi(
                new ToolBlockModel(
                    "bash",
                    "",
                    ToolBlockStatus.Ok,
                    null,
                    null,
                    ExitCode: 0,
                    Duration: TimeSpan.FromMilliseconds(400)
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
            "\u2713 edit  [red]src[/].cs \u00b7 +1 \u22121\n"
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
            "\u2713 edit  src/x.cs \u00b7 +1 \u22121\n"
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
            "\u2713 edit  src/x.cs\n",
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

    private static string RenderAscii(ToolBlockModel block)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.Write(
            new ToolBlockRenderer(
                new TerminalCapabilities(Spectre.Console.ColorSystemSupport.Detect, Unicode: false)
            ).Render(block)
        );
        return console.Output;
    }
}
