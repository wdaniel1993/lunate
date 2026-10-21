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

    private static readonly StatusFooterModel Everything = new(
        "deepseek-v4.1-flash-extended-for-tests",
        12400,
        128000,
        "~/dev/lunate",
        "main"
    )
    {
        ThinkLevel = "medium",
        FavoritePosition = 2,
        FavoriteCount = 5,
        TurnCost = "$0.0123",
    };

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
    public void The_standard_band_then_drops_the_directory()
    {
        StatusFooterModel footer = Full with { Model = "deepseek-v4.1-flash-extended" };

        Assert.Equal(
            "deepseek-v4.1-flash-extended · 12.4k/128.0k (10%)\n",
            Render(footer, width: 60)
        );
    }

    [Theory]
    [InlineData(40)]
    [InlineData(59)]
    public void The_compact_footer_shows_only_the_model_and_percent(int width) =>
        Assert.Equal("deepseek-v4.1-flash \u00b7 10%\n", Render(Full, width));

    [Fact]
    public void The_compact_footer_drops_every_optional_segment() =>
        Assert.Equal(
            "deepseek-v4.1-flash \u00b7 10%",
            StatusFooterRenderer.PlainText(
                Full with
                {
                    ThinkLevel = "medium",
                },
                width: 40,
                ["widget-a", "widget-b"]
            )
        );

    [Fact]
    public void The_compact_footer_omits_the_percent_without_a_window() =>
        Assert.Equal(
            "report-model\n",
            Render(new StatusFooterModel("report-model", 5, 0, "/repo", null), width: 40)
        );

    [Fact]
    public void The_standard_band_keeps_the_token_counts_at_sixty() =>
        Assert.Equal(
            "deepseek-v4.1-flash \u00b7 12.4k/128.0k (10%) \u00b7 ~/dev/lunate\n",
            Render(Full, width: 60)
        );

    [Fact]
    public void The_full_band_keeps_every_segment_at_eighty() =>
        Assert.Equal(
            "deepseek-v4.1-flash \u00b7 12.4k/128.0k (10%) \u00b7 ~/dev/lunate \u00b7 main\n",
            Render(Full, width: 80)
        );

    [Fact]
    public void Model_and_percent_survive_even_extreme_widths()
    {
        string output = Render(Full, width: 5);

        Assert.Equal("deepseek-v4.1-flash · 10%\n", output);
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

    [Fact]
    public void Widget_segments_append_after_the_core_fields() =>
        Assert.Equal(
            "deepseek-v4.1-flash · 12.4k/128.0k (10%) · ~/dev/lunate · main · widget-a · widget-b",
            StatusFooterRenderer.PlainText(Full, width: 200, ["widget-a", "widget-b"])
        );

    [Fact]
    public void Widget_segments_drop_before_the_branch() =>
        Assert.Equal(
            "deepseek-v4.1-flash · 12.4k/128.0k (10%) · ~/dev/lunate · main",
            StatusFooterRenderer.PlainText(Full, width: 70, ["widget-a"])
        );

    [Fact]
    public void Empty_widget_segments_are_absent() =>
        Assert.Equal(
            "deepseek-v4.1-flash · 12.4k/128.0k (10%) · ~/dev/lunate · main · widget-a",
            StatusFooterRenderer.PlainText(Full, width: 200, ["", "widget-a"])
        );

    [Fact]
    public void Favourites_render_after_the_usage_segment() =>
        Assert.Equal(
            "deepseek-v4.1-flash · 12.4k/128.0k (10%) · fav:2/5 · ~/dev/lunate · main\n",
            Render(Full with { FavoritePosition = 2, FavoriteCount = 5 }, width: 80)
        );

    [Fact]
    public void Turn_cost_renders_after_the_favourites_segment() =>
        Assert.Equal(
            "deepseek-v4.1-flash · 12.4k/128.0k (10%) · fav:2/5 · $0.0123 · ~/dev/lunate · main\n",
            Render(
                Full with
                {
                    FavoritePosition = 2,
                    FavoriteCount = 5,
                    TurnCost = "$0.0123",
                },
                width: 100
            )
        );

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Absent_turn_cost_omits_the_cost_segment(string? turnCost) =>
        Assert.Equal(
            "deepseek-v4.1-flash · 12.4k/128.0k (10%) · fav:2/5 · ~/dev/lunate · main\n",
            Render(
                Full with
                {
                    FavoritePosition = 2,
                    FavoriteCount = 5,
                    TurnCost = turnCost,
                },
                width: 100
            )
        );

    [Theory]
    [InlineData(null, 5)]
    [InlineData(0, 5)]
    [InlineData(6, 5)]
    [InlineData(2, 0)]
    public void Out_of_range_favourites_omit_the_segment(int? position, int count) =>
        Assert.Equal(
            "deepseek-v4.1-flash · 12.4k/128.0k (10%) · ~/dev/lunate · main\n",
            Render(Full with { FavoritePosition = position, FavoriteCount = count }, width: 100)
        );

    [Theory]
    [InlineData(
        120,
        "deepseek-v4.1-flash-extended-for-tests · 12.4k/128.0k (10%) · fav:2/5 · $0.0123 · think:medium · ~/dev/lunate · main"
    )]
    [InlineData(
        110,
        "deepseek-v4.1-flash-extended-for-tests · 12.4k/128.0k (10%) · fav:2/5 · $0.0123 · think:medium · ~/dev/lunate"
    )]
    [InlineData(
        95,
        "deepseek-v4.1-flash-extended-for-tests · 12.4k/128.0k (10%) · fav:2/5 · $0.0123 · think:medium"
    )]
    [InlineData(
        80,
        "deepseek-v4.1-flash-extended-for-tests · 12.4k/128.0k (10%) · fav:2/5 · $0.0123"
    )]
    [InlineData(70, "deepseek-v4.1-flash-extended-for-tests · 12.4k/128.0k (10%) · $0.0123")]
    [InlineData(60, "deepseek-v4.1-flash-extended-for-tests · 12.4k/128.0k (10%)")]
    public void Narrow_width_drops_in_priority_order(int width, string expected) =>
        Assert.Equal(expected, StatusFooterRenderer.PlainText(Everything, width, ["widget-a"]));

    [Fact]
    public void The_compact_footer_drops_favourites_and_cost() =>
        Assert.Equal(
            "deepseek-v4.1-flash \u00b7 10%\n",
            Render(Full with { FavoritePosition = 2, FavoriteCount = 5, TurnCost = "$0.0123" }, 40)
        );

    [Fact]
    public void Think_level_appears_after_the_usage_segment() =>
        Assert.Equal(
            "deepseek-v4.1-flash · 12.4k/128.0k (10%) · think:medium · ~/dev/lunate · main\n",
            Render(Full with { ThinkLevel = "medium" }, width: 80)
        );

    [Fact]
    public void Absent_think_level_omits_the_segment() =>
        Assert.Equal(
            "deepseek-v4.1-flash · 12.4k/128.0k (10%) · ~/dev/lunate · main\n",
            Render(Full with { ThinkLevel = null }, width: 80)
        );

    [Fact]
    public void Think_level_drops_after_the_directory_and_before_the_usage()
    {
        StatusFooterModel footer = Full with { ThinkLevel = "medium" };

        Assert.Equal(
            "deepseek-v4.1-flash · 12.4k/128.0k (10%) · think:medium · ~/dev/lunate\n",
            Render(footer, width: 70)
        );
        Assert.Equal(
            "deepseek-v4.1-flash · 12.4k/128.0k (10%) · think:medium\n",
            Render(footer, width: 60)
        );
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
