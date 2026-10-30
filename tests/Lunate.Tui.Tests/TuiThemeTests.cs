using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// The named themes the <c>/settings</c> surface offers: <c>default</c> keeps whatever the
/// terminal reports, <c>ansi16</c> pins the high-contrast sixteen-colour system, and <c>mono</c>
/// drops every attribute — the no-colour theme. <c>NO_COLOR</c> wins over any of them: a plan can
/// only lower the colour depth, never raise it back.
/// </summary>
public sealed class TuiThemeTests
{
    [Theory]
    [InlineData("default")]
    [InlineData("ansi16")]
    [InlineData("mono")]
    public void The_three_named_themes_resolve(string name) =>
        Assert.True(TuiTheme.TryGet(name, out _));

    [Fact]
    public void An_unknown_name_is_not_a_theme() => Assert.False(TuiTheme.TryGet("neon", out _));

    [Fact]
    public void The_theme_names_are_the_accepted_setting_literals()
    {
        Assert.Equal("'default', 'ansi16' or 'mono'", TuiTheme.Literals);
    }

    [Fact]
    public void Default_keeps_the_detected_colour_system()
    {
        TuiTheme theme = TuiTheme.Get("default");
        Assert.Equal(ColorSystemSupport.Detect, theme.Plan(ColorSystemSupport.Detect));
        Assert.Equal(ColorSystemSupport.TrueColor, theme.Plan(ColorSystemSupport.TrueColor));
        Assert.Equal(ColorSystemSupport.NoColors, theme.Plan(ColorSystemSupport.NoColors));
    }

    [Theory]
    [InlineData(ColorSystemSupport.Detect)]
    [InlineData(ColorSystemSupport.TrueColor)]
    [InlineData(ColorSystemSupport.EightBit)]
    public void Ansi16_pins_the_sixteen_colour_system(ColorSystemSupport detected)
    {
        TuiTheme theme = TuiTheme.Get("ansi16");
        Assert.Equal(ColorSystemSupport.Standard, theme.Plan(detected));
    }

    [Fact]
    public void Ansi16_never_raises_colour_above_no_colour()
    {
        TuiTheme theme = TuiTheme.Get("ansi16");
        Assert.Equal(ColorSystemSupport.NoColors, theme.Plan(ColorSystemSupport.NoColors));
    }

    [Theory]
    [InlineData(ColorSystemSupport.Detect)]
    [InlineData(ColorSystemSupport.TrueColor)]
    [InlineData(ColorSystemSupport.EightBit)]
    public void Mono_drops_every_attribute(ColorSystemSupport detected)
    {
        TuiTheme theme = TuiTheme.Get("mono");
        Assert.Equal(ColorSystemSupport.NoColors, theme.Plan(detected));
    }

    [Fact]
    public void Mono_keeps_a_colourless_terminal_colourless()
    {
        TuiTheme theme = TuiTheme.Get("mono");
        Assert.Equal(ColorSystemSupport.NoColors, theme.Plan(ColorSystemSupport.NoColors));
    }
}
