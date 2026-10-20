using Spectre.Console;
using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

/// <summary>
/// The committed user block: the <c>&gt; </c> prefix in the user colour, plain text, a two-space
/// hanging indent on wrapped lines, clipping for long words and one blank line after.
/// </summary>
public sealed class UserEchoRendererTests
{
    [Fact]
    public void A_short_message_renders_the_prefix_and_one_blank_line()
    {
        Assert.Equal("> hello\n\n", Render("hello", width: 40));
    }

    [Fact]
    public void A_wrapped_message_keeps_the_hanging_indent()
    {
        Assert.Equal(
            "> one two\n  three four\n  five six\n\n",
            Render("one two three four five six", width: 12)
        );
    }

    [Fact]
    public void A_word_longer_than_the_width_clips_instead_of_breaking()
    {
        Assert.Equal("> " + new string('a', 8) + "\n\n", Render(new string('a', 20), width: 10));
    }

    [Fact]
    public void A_hard_break_continues_with_the_hanging_indent()
    {
        Assert.Equal("> one\n  two\n\n", Render("one\ntwo", width: 40));
    }

    [Fact]
    public void Markup_in_the_message_stays_literal()
    {
        Assert.Equal("> see [red]here[/]\n\n", Render("see [red]here[/]", width: 40));
    }

    [Fact]
    public void Leading_and_trailing_spaces_survive_the_echo()
    {
        Assert.Equal(">   hi  \n\n", Render("  hi  ", width: 40));
    }

    [Fact]
    public void Consecutive_spaces_survive_the_echo()
    {
        Assert.Equal("> a  b\n\n", Render("a  b", width: 40));
    }

    [Fact]
    public void A_blank_hard_line_renders_as_an_indent_only_line()
    {
        Assert.Equal("> a\n  \n  b\n\n", Render("a\n\nb", width: 40));
    }

    [Fact]
    public void A_tab_survives_the_echo()
    {
        Assert.Equal("> a\tb\n\n", Render("a\tb", width: 40));
    }

    [Fact]
    public void An_empty_message_renders_the_bare_prefix_line()
    {
        Assert.Equal("> \n\n", Render(string.Empty, width: 40));
    }

    [Fact]
    public void The_prefix_carries_the_user_colour_and_the_text_stays_plain()
    {
        Assert.Equal(
            "\u001b[38;2;135;175;215m> \u001b[0mhello\n\n",
            RenderAnsi("hello", width: 40)
        );
    }

    [Fact]
    public void No_colours_drop_the_attribute_and_keep_the_text_identical()
    {
        string plain = Render("one two three four five six", width: 12);

        string ansi = RenderAnsi(
            "one two three four five six",
            width: 12,
            colors: ColorSystem.NoColors
        );

        Assert.Equal(plain, ansi);
        Assert.DoesNotContain("\u001b", ansi, StringComparison.Ordinal);
    }

    [Fact]
    public void Sixteen_colour_mode_paints_the_prefix_ansi_blue()
    {
        var console = new TestConsole();
        console.Profile.Width = 200;
        console.EmitAnsiSequences = true;
        console.Profile.Capabilities.ColorSystem = ColorSystem.Standard;
        console.Write(
            new UserEchoRenderer().Render("hello", width: 40, ColorSystemSupport.Standard)
        );

        Assert.Equal("\u001b[94m> \u001b[0mhello\n\n", console.Output.ReplaceLineEndings("\n"));
    }

    private static string Render(string text, int width)
    {
        var console = new TestConsole();
        console.Profile.Width = 200;
        console.Write(new UserEchoRenderer().Render(text, width));
        return console.Output.ReplaceLineEndings("\n");
    }

    private static string RenderAnsi(
        string text,
        int width,
        ColorSystem colors = ColorSystem.TrueColor
    )
    {
        var console = new TestConsole();
        console.Profile.Width = 200;
        console.EmitAnsiSequences = true;
        console.Profile.Capabilities.ColorSystem = colors;
        console.Write(new UserEchoRenderer().Render(text, width));
        return console.Output.ReplaceLineEndings("\n");
    }
}
