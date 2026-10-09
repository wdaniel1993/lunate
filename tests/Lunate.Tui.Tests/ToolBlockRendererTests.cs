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
                null
            )
        );

        Assert.Equal(
            "tool edit [red]src[/].cs ok\n[dim]not markup[/]\n[bold]neither[/]\n[[nested]]\n",
            output
        );
    }

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
