using Spectre.Console;
using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

public sealed class ApprovalPromptRendererTests
{
    [Fact]
    public void Null_model_is_rejected() =>
        Assert.Throws<ArgumentNullException>(() => new ApprovalPromptRenderer().Render(null!));

    [Fact]
    public void Headline_shows_the_summarized_action_when_no_detail_is_ready() =>
        Assert.Equal(
            "\u26a0 bash  dotnet format\n" + "Allow bash?  [y]es  [n]o  [a]lways this session\n",
            Render(new ApprovalPromptModel("bash", """{"command":"dotnet format"}"""))
        );

    [Fact]
    public void Detail_action_appears_only_in_the_headline()
    {
        string output = Render(
            new ApprovalPromptModel("bash", "ignored summary", Detail: ["dotnet format"])
        );

        Assert.Equal(
            "\u26a0 bash  dotnet format\n" + "Allow bash?  [y]es  [n]o  [a]lways this session\n",
            output
        );
    }

    [Fact]
    public void Detail_body_lines_are_indented_under_the_headline() =>
        Assert.Equal(
            "\u26a0 bash  dotnet format\n"
                + "  line one\n"
                + "  line two\n"
                + "Allow bash?  [y]es  [n]o  [a]lways this session\n",
            Render(
                new ApprovalPromptModel(
                    "bash",
                    string.Empty,
                    Detail: ["dotnet format", "line one", "line two"]
                )
            )
        );

    [Fact]
    public void Detail_body_is_bounded_to_five_lines_with_a_marker() =>
        Assert.Equal(
            "\u26a0 bash  dotnet format\n"
                + "  one\n"
                + "  two\n"
                + "  three\n"
                + "  four\n"
                + "  five\n"
                + "  \u2026 +3 lines\n"
                + "Allow bash?  [y]es  [n]o  [a]lways this session\n",
            Render(
                new ApprovalPromptModel(
                    "bash",
                    string.Empty,
                    Detail:
                    [
                        "dotnet format",
                        "one",
                        "two",
                        "three",
                        "four",
                        "five",
                        "six",
                        "seven",
                        "eight",
                    ]
                )
            )
        );

    [Fact]
    public void Detail_body_at_the_line_budget_is_kept_intact() =>
        Assert.Equal(
            "\u26a0 bash  dotnet format\n"
                + "  one\n"
                + "  two\n"
                + "  three\n"
                + "  four\n"
                + "  five\n"
                + "  six\n"
                + "Allow bash?  [y]es  [n]o  [a]lways this session\n",
            Render(
                new ApprovalPromptModel(
                    "bash",
                    string.Empty,
                    Detail: ["dotnet format", "one", "two", "three", "four", "five", "six"]
                )
            )
        );

    [Fact]
    public void Workspace_root_line_follows_the_detail() =>
        Assert.Equal(
            "\u26a0 bash  dotnet format\n"
                + "  line one\n"
                + "  in ~/worktree-b\n"
                + "Allow bash?  [y]es  [n]o  [a]lways this session\n",
            Render(
                new ApprovalPromptModel(
                    "bash",
                    string.Empty,
                    Detail: ["dotnet format", "line one"],
                    WorkspaceRoot: "~/worktree-b"
                )
            )
        );

    [Fact]
    public void Workspace_root_line_is_omitted_when_unset() =>
        Assert.DoesNotContain(
            "in ",
            Render(new ApprovalPromptModel("bash", string.Empty, Detail: ["dotnet format"])),
            StringComparison.Ordinal
        );

    [Fact]
    public void Subagent_asker_prefixes_the_question() =>
        Assert.Equal(
            "\u26a0 bash  dotnet format\n"
                + "subagent (depth 1) asks: Allow bash?  [y]es  [n]o  [a]lways this session\n",
            Render(
                new ApprovalPromptModel(
                    "bash",
                    string.Empty,
                    Asker: "subagent (depth 1)",
                    Detail: ["dotnet format"]
                )
            )
        );

    [Fact]
    public void Escaping_keeps_bracket_text_literal() =>
        Assert.Equal(
            "\u26a0 [red]bash[/]  [red]echo[/]\n"
                + "  [red]detail[/]\n"
                + "[red]sub[/] asks: Allow [red]bash[/]?  [y]es  [n]o  [a]lways this session\n",
            Render(
                new ApprovalPromptModel(
                    "[red]bash[/]",
                    string.Empty,
                    Asker: "[red]sub[/]",
                    Detail: ["[red]echo[/]", "[red]detail[/]"]
                )
            )
        );

    [Fact]
    public void Headline_is_warning_yellow_in_ansi() =>
        Assert.Contains(
            "\u001b[38;5;11m\u26a0 bash  dotnet format",
            RenderAnsi(new ApprovalPromptModel("bash", string.Empty, Detail: ["dotnet format"]))
        );

    [Fact]
    public void Key_letters_are_palette_accent_in_ansi()
    {
        string output = RenderAnsi(
            new ApprovalPromptModel("bash", string.Empty, Detail: ["dotnet format"])
        );

        Assert.Contains(
            "\u001b[38;2;95;215;215my\u001b[0m]es  ["
                + "\u001b[38;2;95;215;215mn\u001b[0m]o  ["
                + "\u001b[38;2;95;215;215ma\u001b[0m]lways this session",
            output
        );
    }

    [Fact]
    public void Ascii_capabilities_use_the_exclamation_glyph()
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.Write(
            new ApprovalPromptRenderer(
                new TerminalCapabilities(Spectre.Console.ColorSystemSupport.Detect, Unicode: false)
            ).Render(new ApprovalPromptModel("bash", string.Empty, Detail: ["dotnet format"]))
        );

        Assert.Equal(
            "! bash  dotnet format\nAllow bash?  [y]es  [n]o  [a]lways this session\n",
            console.Output
        );
    }

    [Fact]
    public void Plain_lines_clip_to_forty_columns_and_keep_every_choice()
    {
        var lines = ApprovalPromptRenderer.PlainLines(
            new ApprovalPromptModel(
                "bash",
                string.Empty,
                Detail:
                [
                    "dotnet format --verify-no-changes --include src/Lunate/Tui/Approval.cs",
                    "a detail line that is much longer than forty columns of output",
                ],
                WorkspaceRoot: "~/worktree-b"
            ),
            width: 40,
            ColorSystemSupport.NoColors,
            unicode: true
        );

        Assert.Equal(ApprovalPart.Headline, lines[0].Part);
        Assert.StartsWith("\u26a0 bash  dotnet format", lines[0].Text, StringComparison.Ordinal);
        Assert.True(CellText.Width(lines[0].Text) <= 40);
        Assert.Equal(ApprovalPart.Detail, lines[1].Part);
        Assert.True(CellText.Width(lines[1].Text) <= 40);
        Assert.Equal("  in ~/worktree-b", lines[2].Text);
        Assert.Equal(ApprovalPart.Question, lines[3].Part);
        Assert.Equal("Allow bash?", lines[3].Text);
        Assert.Equal("[y]es  [n]o  [a]lways this session", lines[4].Text);
        Assert.Equal(ApprovalPart.Question, lines[4].Part);
        Assert.True(CellText.Width(lines[4].Text) <= 40);
    }

    [Fact]
    public void Plain_lines_at_forty_columns_drop_detail_to_its_first_line()
    {
        var lines = ApprovalPromptRenderer.PlainLines(
            new ApprovalPromptModel(
                "bash",
                string.Empty,
                Detail: ["dotnet format", "line one", "line two", "line three"]
            ),
            width: 40,
            ColorSystemSupport.NoColors,
            unicode: true
        );

        var detail = Assert.Single(lines.Where(line => line.Part == ApprovalPart.Detail));
        Assert.Equal("  line one", detail.Text);
        Assert.DoesNotContain(lines, line => line.Text == "  line two");
        Assert.DoesNotContain(lines, line => line.Text == "  line three");
        Assert.Equal("Allow bash?", lines[2].Text);
        Assert.Equal("[y]es  [n]o  [a]lways this session", lines[3].Text);
        Assert.Equal(34, CellText.Width(lines[3].Text));
    }

    [Fact]
    public void Plain_lines_at_forty_columns_keep_the_workspace_root_line()
    {
        var lines = ApprovalPromptRenderer.PlainLines(
            new ApprovalPromptModel(
                "bash",
                string.Empty,
                Detail: ["dotnet format", "line one", "line two"],
                WorkspaceRoot: "~/worktree-b"
            ),
            width: 40,
            ColorSystemSupport.NoColors,
            unicode: true
        );

        Assert.Equal("  line one", lines[1].Text);
        Assert.Equal("  in ~/worktree-b", lines[2].Text);
        Assert.Equal("Allow bash?", lines[3].Text);
        Assert.Equal("[y]es  [n]o  [a]lways this session", lines[4].Text);
        Assert.Equal(34, CellText.Width(lines[4].Text));
    }

    [Fact]
    public void Plain_lines_at_sixty_columns_keep_the_full_detail()
    {
        var lines = ApprovalPromptRenderer.PlainLines(
            new ApprovalPromptModel(
                "bash",
                string.Empty,
                Detail: ["dotnet format", "line one", "line two", "line three"]
            ),
            width: 60,
            ColorSystemSupport.NoColors,
            unicode: true
        );

        Assert.Equal("  line one", lines[1].Text);
        Assert.Equal("  line two", lines[2].Text);
        Assert.Equal("  line three", lines[3].Text);
    }

    [Fact]
    public void Plain_lines_at_forty_columns_clip_the_headline_not_the_choices()
    {
        var lines = ApprovalPromptRenderer.PlainLines(
            new ApprovalPromptModel(
                "bash",
                string.Empty,
                Detail: ["dotnet format --verify-no-changes --include src/Lunate/Tui/Approval.cs"]
            ),
            width: 40,
            ColorSystemSupport.NoColors,
            unicode: true
        );

        Assert.StartsWith("\u26a0 bash  dotnet format", lines[0].Text, StringComparison.Ordinal);
        Assert.Equal(40, CellText.Width(lines[0].Text));
        Assert.Equal("[y]es  [n]o  [a]lways this session", lines[^1].Text);
        Assert.Equal(34, CellText.Width(lines[^1].Text));
    }

    [Fact]
    public void Plain_lines_carry_the_warning_muted_and_accent_tones()
    {
        var lines = ApprovalPromptRenderer.PlainLines(
            new ApprovalPromptModel("bash", string.Empty, Detail: ["dotnet format", "body"]),
            width: 80,
            ColorSystemSupport.TrueColor,
            unicode: true
        );

        Assert.Equal("\u001b[38;2;255;255;0m\u26a0 bash  dotnet format\u001b[0m", lines[0].Text);
        Assert.Equal("\u001b[38;2;138;138;138m  body\u001b[0m", lines[1].Text);
        Assert.EndsWith(
            "\u001b[38;2;95;215;215ma\u001b[0m]lways this session",
            lines[2].Text,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Render_at_forty_columns_puts_the_question_above_the_choices()
    {
        var console = new TestConsole();
        console.Profile.Width = 40;
        console.Write(
            new ApprovalPromptRenderer().Render(
                new ApprovalPromptModel(
                    "bash",
                    string.Empty,
                    Detail:
                    [
                        "dotnet format --verify-no-changes --include src/Lunate/Tui/Approval.cs",
                        "a detail line that is much longer than forty columns of output",
                    ]
                ),
                width: 40
            )
        );

        string[] lines = console.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("Allow bash?", lines);
        Assert.Contains("[y]es  [n]o  [a]lways this session", lines);
    }

    private static string Render(ApprovalPromptModel prompt)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.Write(new ApprovalPromptRenderer().Render(prompt));
        return console.Output;
    }

    private static string RenderAnsi(ApprovalPromptModel prompt)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.EmitAnsiSequences = true;
        console.Write(new ApprovalPromptRenderer().Render(prompt));
        return console.Output;
    }
}
