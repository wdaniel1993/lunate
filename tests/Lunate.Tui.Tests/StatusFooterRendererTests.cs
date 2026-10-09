using System.Globalization;
using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

public sealed class StatusFooterRendererTests
{
    private static readonly StatusFooterModel Full = new(
        "deepseek-v4.1-flash",
        12400,
        128000,
        "~/dev/lunate",
        "main"
    );

    [Fact]
    public void Null_model_is_rejected() =>
        Assert.Throws<ArgumentNullException>(() => new StatusFooterRenderer().Render(null!, 80));

    [Fact]
    public void Full_footer_renders_one_dim_line() =>
        Assert.Equal(
            "deepseek-v4.1-flash · 12.4k/128.0k (10%) · ~/dev/lunate · main\n",
            Render(Full, width: 80)
        );

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1000, "1.0k")]
    [InlineData(12400, "12.4k")]
    [InlineData(128000, "128.0k")]
    [InlineData(999949, "999.9k")]
    [InlineData(999999, "1.0M")]
    [InlineData(1000000, "1.0M")]
    [InlineData(1260000, "1.3M")]
    public void Tokens_are_compact_and_culture_invariant(long tokens, string expected) =>
        Assert.Equal(expected, StatusFooterRenderer.FormatTokens(tokens));

    [Fact]
    public void Percent_rounds_to_an_integer() =>
        Assert.Contains("(10%)", Render(Full, width: 80), StringComparison.Ordinal);

    [Fact]
    public void Zero_window_omits_percent()
    {
        var footer = new StatusFooterModel("report-model", 5, 0, "/repo", null);

        Assert.Equal("report-model · 5/0 · /repo\n", Render(footer, width: 80));
    }

    [Fact]
    public void Missing_branch_omits_the_branch_segment()
    {
        var footer = new StatusFooterModel("m", 1, 2, "/repo", null);

        Assert.Equal("m · 1/2 (50%) · /repo\n", Render(footer, width: 80));
    }

    [Fact]
    public void Narrow_width_drops_the_branch_first()
    {
        string output = Render(Full, width: 60);

        Assert.Equal("deepseek-v4.1-flash · 12.4k/128.0k (10%) · ~/dev/lunate\n", output);
    }

    [Fact]
    public void Narrower_width_then_drops_the_directory()
    {
        string output = Render(Full, width: 50);

        Assert.Equal("deepseek-v4.1-flash · 12.4k/128.0k (10%)\n", output);
    }

    [Fact]
    public void Model_and_usage_survive_even_extreme_widths()
    {
        string output = Render(Full, width: 5);

        Assert.Equal("deepseek-v4.1-flash · 12.4k/128.0k (10%)\n", output);
    }

    [Fact]
    public void Escaping_keeps_bracket_text_literal()
    {
        var footer = new StatusFooterModel("[red]m[/]", 1, 2, "[bold]~/x[/]", "[dim]b[/]");

        Assert.Equal("[red]m[/] · 1/2 (50%) · [bold]~/x[/] · [dim]b[/]\n", Render(footer, 200));
    }

    [Fact]
    public void Footer_line_is_dim() =>
        Assert.StartsWith("\u001b[2m", RenderAnsi(Full, width: 80), StringComparison.Ordinal);

    [Fact]
    public void Formatting_ignores_the_machine_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-AT");

            Assert.Equal(
                "deepseek-v4.1-flash · 12.4k/128.0k (10%) · ~/dev/lunate · main\n",
                Render(Full, width: 80)
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static string Render(StatusFooterModel footer, int width)
    {
        var console = new TestConsole();
        console.Profile.Width = 200;
        console.Write(new StatusFooterRenderer().Render(footer, width));
        return console.Output;
    }

    private static string RenderAnsi(StatusFooterModel footer, int width)
    {
        var console = new TestConsole();
        console.Profile.Width = 200;
        console.EmitAnsiSequences = true;
        console.Write(new StatusFooterRenderer().Render(footer, width));
        return console.Output;
    }
}
