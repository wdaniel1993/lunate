using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

public sealed class MarkdownRendererTests
{
    [Theory]
    [InlineData("| a | b |\n| - | - |\n| 1 | 2 |", "| a | b |\n| - | - |\n| 1 | 2 |\n")]
    [InlineData("[click](https://example.com)", "click (https://example.com)\n")]
    [InlineData("![alt](https://example.com/i.png)", "alt (https://example.com/i.png)\n")]
    [InlineData("<b>bold</b>", "<b>bold</b>\n")]
    [InlineData("<div>\nraw\n</div>", "<div>\nraw\n</div>\n")]
    [InlineData("- [ ] todo\n- [x] done", "• [ ] todo\n• [x] done\n")]
    public void Unsupported_constructs_render_readable_plain_text_without_markup(
        string markdown,
        string expected
    ) => Assert.Equal(expected, Render(markdown));

    [Theory]
    [InlineData("[dim]not markup[/]")]
    [InlineData("[bold]neither[/]")]
    [InlineData("lone [ bracket and ]")]
    [InlineData("`unterminated")]
    [InlineData("**unclosed emphasis")]
    [InlineData("<unclosed tag")]
    public void Adversarial_text_never_throws_and_keeps_its_characters(string markdown)
    {
        string output = Render(markdown);

        Assert.EndsWith("\n", output);
        foreach (char significant in markdown.Where(static c => !char.IsWhiteSpace(c)))
        {
            Assert.Contains(significant, output);
        }
    }

    [Fact]
    public void Escaping_keeps_bracket_text_literal()
    {
        Assert.Equal("[dim]not markup[/]\n", Render("[dim]not markup[/]"));
        Assert.Equal("[red]x[/] stays\n", Render("[red]x[/] stays"));
    }

    [Fact]
    public void Empty_and_blank_documents_render_nothing()
    {
        Assert.Equal(string.Empty, Render(string.Empty));
        Assert.Equal(string.Empty, Render("   \n\n  \n"));
    }

    [Fact]
    public void Link_reference_definitions_do_not_render()
    {
        Assert.Equal("one\n\ntwo\n", Render("one\n\n[ref]: https://example.com\n\ntwo"));
    }

    [Fact]
    public void Shortcut_reference_links_render_as_plain_label_and_url()
    {
        Assert.Equal(
            "one ref (https://example.com)\n",
            Render("one [ref]\n\n[ref]: https://example.com")
        );
    }

    [Fact]
    public void Null_markdown_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new MarkdownRenderer().Render(null!));
    }

    [Fact]
    public void Bold_uses_the_bold_ansi_style() =>
        Assert.Contains("\u001b[1m", RenderAnsi("**bold**"));

    [Fact]
    public void Italic_uses_the_italic_ansi_style() =>
        Assert.Contains("\u001b[3m", RenderAnsi("*italic*"));

    [Fact]
    public void First_level_headings_are_bold_and_underlined()
    {
        string output = RenderAnsi("# Heading");

        Assert.Contains("\u001b[1;4m", output);
    }

    [Fact]
    public void Inline_code_uses_the_aqua_ansi_style() =>
        Assert.Contains("\u001b[38;5;14m", RenderAnsi("`code`"));

    [Fact]
    public void Quotes_use_the_dim_ansi_style() => Assert.Contains("\u001b[2m", RenderAnsi("> q"));

    [Theory]
    [InlineData("```nix\nlet x = 1;\n```", "nix\nlet x = 1;\n")]
    [InlineData("```\nplain\n```", "plain\n")]
    public void Untagged_or_unknown_fences_render_plain_with_the_label(
        string markdown,
        string expected
    ) => Assert.Equal(expected, Render(markdown));

    [Fact]
    public void CSharp_keywords_use_the_blue_ansi_style() =>
        Assert.Contains("\u001b[38;5;12m", RenderAnsi("```csharp\nvar x = 1;\n```"));

    [Fact]
    public void CSharp_comments_use_the_grey_ansi_style() =>
        Assert.Contains("\u001b[38;5;8m", RenderAnsi("```csharp\n// note\n```"));

    [Fact]
    public void Json_keys_use_the_blue_ansi_style() =>
        Assert.Contains("\u001b[38;5;12m", RenderAnsi("```json\n{\"k\": \"v\"}\n```"));

    [Fact]
    public void Shell_variables_use_the_yellow_ansi_style() =>
        Assert.Contains("\u001b[38;5;11m", RenderAnsi("```sh\necho $HOME\n```"));

    private static string Render(string markdown)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.Write(new MarkdownRenderer().Render(markdown));
        return console.Output;
    }

    private static string RenderAnsi(string markdown)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.EmitAnsiSequences = true;
        console.Write(new MarkdownRenderer().Render(markdown));
        return console.Output;
    }
}
