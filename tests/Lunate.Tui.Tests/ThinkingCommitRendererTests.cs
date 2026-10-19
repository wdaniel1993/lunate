using Spectre.Console;
using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

/// <summary>
/// The committed thinking block's renderer contract: the collapsed <c>✦ thought for &lt;n&gt;s</c>
/// line, the full reasoning text wrapped with a two-space indent, markup escaping, and the
/// thinking colour by the console's colour system (dropped under NO_COLOR).
/// </summary>
public sealed class ThinkingCommitRendererTests
{
    [Fact]
    public void Null_model_is_rejected() =>
        Assert.Throws<ArgumentNullException>(() => new ThinkingCommitRenderer().Render(null!, 40));

    [Fact]
    public void The_collapsed_commit_is_one_thought_for_line()
    {
        var commit = new ThinkingCommitModel("weighing the options", 12, Full: false);

        Assert.Equal("✦ thought for 12s\n", Render(commit, width: 40));
    }

    [Fact]
    public void The_full_commit_wraps_at_word_boundaries_with_a_two_space_indent()
    {
        var commit = new ThinkingCommitModel(
            "one two three four five six seven eight nine ten",
            4,
            Full: true
        );

        Assert.Equal(
            "  one two three four\n  five six seven\n  eight nine ten\n",
            Render(commit, width: 20)
        );
    }

    [Fact]
    public void A_run_longer_than_the_width_breaks_mid_run()
    {
        var commit = new ThinkingCommitModel(new string('a', 30), 0, Full: true);

        Assert.Equal(
            "  " + new string('a', 18) + "\n  " + new string('a', 12) + "\n",
            Render(commit, width: 20)
        );
    }

    [Fact]
    public void Markup_in_the_reasoning_text_stays_literal()
    {
        var commit = new ThinkingCommitModel("see [red]here[/]", 0, Full: true);

        Assert.Equal("  see [red]here[/]\n", Render(commit, width: 40));
    }

    [Fact]
    public void The_commit_carries_the_thinking_colour_on_every_line()
    {
        var commit = new ThinkingCommitModel("one two three four five", 0, Full: true);

        Assert.Equal(
            "\u001b[38;2;175;135;215m  one two three four\u001b[0m\n\u001b[38;2;175;135;215m  five\u001b[0m\n",
            RenderAnsi(commit, width: 20)
        );
    }

    [Fact]
    public void No_colours_drop_the_attribute_and_keep_the_text_identical()
    {
        var full = new ThinkingCommitModel("weighing the options", 12, Full: true);
        var collapsed = new ThinkingCommitModel("weighing the options", 12, Full: false);

        string plainFull = RenderAnsi(full, width: 40, colors: ColorSystem.NoColors);
        string plainCollapsed = RenderAnsi(collapsed, width: 40, colors: ColorSystem.NoColors);

        Assert.Equal(Render(full, width: 40), plainFull);
        Assert.Equal(Render(collapsed, width: 40), plainCollapsed);
        Assert.DoesNotContain("\u001b", plainFull + plainCollapsed, StringComparison.Ordinal);
    }

    [Fact]
    public void Sixteen_colour_mode_paints_ansi_magenta_instead_of_the_console_conversion()
    {
        var commit = new ThinkingCommitModel("weighing the options", 12, Full: false);

        var console = new TestConsole();
        console.Profile.Width = 200;
        console.EmitAnsiSequences = true;
        console.Profile.Capabilities.ColorSystem = ColorSystem.Standard;
        console.Write(
            new ThinkingCommitRenderer().Render(commit, width: 40, ColorSystemSupport.Detect)
        );

        Assert.Equal("\u001b[35m✦ thought for 12s\u001b[0m\n", console.Output);
    }

    [Fact]
    public void The_no_colour_capability_stays_plain_even_on_a_colour_console()
    {
        var commit = new ThinkingCommitModel("weighing the options", 12, Full: false);

        var console = new TestConsole();
        console.Profile.Width = 200;
        console.EmitAnsiSequences = true;
        console.Profile.Capabilities.ColorSystem = ColorSystem.TrueColor;
        console.Write(
            new ThinkingCommitRenderer().Render(commit, width: 40, ColorSystemSupport.NoColors)
        );

        Assert.Equal("✦ thought for 12s\n", console.Output);
    }

    private static string Render(ThinkingCommitModel commit, int width)
    {
        var console = new TestConsole();
        console.Profile.Width = 200;
        console.Write(new ThinkingCommitRenderer().Render(commit, width));
        return console.Output;
    }

    private static string RenderAnsi(
        ThinkingCommitModel commit,
        int width,
        ColorSystem colors = ColorSystem.TrueColor
    )
    {
        var console = new TestConsole();
        console.Profile.Width = 200;
        console.EmitAnsiSequences = true;
        console.Profile.Capabilities.ColorSystem = colors;
        console.Write(new ThinkingCommitRenderer().Render(commit, width));
        return console.Output;
    }
}
