using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

public sealed class ApprovalPromptRendererTests
{
    [Fact]
    public void Null_model_is_rejected() =>
        Assert.Throws<ArgumentNullException>(() => new ApprovalPromptRenderer().Render(null!));

    [Fact]
    public void Prompt_names_the_tool_summary_and_the_three_choices() =>
        Assert.Equal(
            "Allow bash dotnet format?  [y]es  [n]o  [a]lways this session\n",
            Render(new ApprovalPromptModel("bash", "dotnet format"))
        );

    [Fact]
    public void Raw_json_args_are_summarized() =>
        Assert.Equal(
            "Allow bash dotnet format?  [y]es  [n]o  [a]lways this session\n",
            Render(new ApprovalPromptModel("bash", """{"command":"dotnet format"}"""))
        );

    [Fact]
    public void Empty_summary_keeps_the_question_marker_attached_to_the_tool() =>
        Assert.Equal(
            "Allow read?  [y]es  [n]o  [a]lways this session\n",
            Render(new ApprovalPromptModel("read", ""))
        );

    [Fact]
    public void Escaping_keeps_bracket_text_literal()
    {
        string output = Render(
            new ApprovalPromptModel("[red]bash[/]", """{"command":"echo [dim]hi[/]"}""")
        );

        Assert.Equal(
            "Allow [red]bash[/] echo [dim]hi[/]?  [y]es  [n]o  [a]lways this session\n",
            output
        );
    }

    [Fact]
    public void Question_marker_is_yellow() =>
        Assert.Contains(
            "\u001b[38;5;11m?",
            RenderAnsi(new ApprovalPromptModel("bash", "dotnet format"))
        );

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
