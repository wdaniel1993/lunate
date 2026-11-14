using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// The three colour forms a theme file may use — an ANSI name, a 256-colour index and a hex triple
/// — and what each renders as at every colour depth. A name renders as its name (the palette's
/// older, name-based spans), a hex value downgrades to the nearest xterm entry and then to the
/// nearest of the sixteen, and <c>NO_COLOR</c> leaves no colour at all.
/// </summary>
public sealed class ThemeColorTests
{
    [Theory]
    [InlineData("red", "red")]
    [InlineData("bright-blue", "blue")]
    [InlineData("teal", "teal")]
    public void An_ansi_name_parses_and_keeps_its_name(string value, string name)
    {
        Assert.True(ThemeColor.TryParse(value, out ThemeColor color, out string? error));
        Assert.Null(error);
        Assert.True(color.IsNamed);
        Assert.Equal(name, color.MarkupColor(ColorSystemSupport.TrueColor));
        Assert.Equal(name, color.MarkupColor(ColorSystemSupport.Standard));
    }

    [Fact]
    public void A_named_colour_keeps_its_exact_xterm_entry_under_256_colours()
    {
        Assert.True(ThemeColor.TryParse("bright-blue", out ThemeColor color, out _));

        Assert.Equal("\u001b[38;5;12m", color.Sgr(ColorSystemSupport.TrueColor));
        Assert.Equal("\u001b[38;5;12m", color.Sgr(ColorSystemSupport.EightBit));
        Assert.Equal("\u001b[94m", color.Sgr(ColorSystemSupport.Standard));
    }

    [Theory]
    [InlineData("#ff8800", "\u001b[38;2;255;136;0m", "\u001b[38;5;208m", "\u001b[93m")]
    [InlineData("#5fd7d7", "\u001b[38;2;95;215;215m", "\u001b[38;5;80m", "\u001b[37m")]
    [InlineData("#000000", "\u001b[38;2;0;0;0m", "\u001b[38;5;0m", "\u001b[30m")]
    public void A_hex_value_parses_and_downgrades_by_capability(
        string value,
        string trueColor,
        string eightBit,
        string sixteen
    )
    {
        Assert.True(ThemeColor.TryParse(value, out ThemeColor color, out string? error));
        Assert.Null(error);

        Assert.Equal(trueColor, color.Sgr(ColorSystemSupport.TrueColor));
        Assert.Equal(eightBit, color.Sgr(ColorSystemSupport.EightBit));
        Assert.Equal(sixteen, color.Sgr(ColorSystemSupport.Standard));
        Assert.Null(color.Sgr(ColorSystemSupport.NoColors));
    }

    [Theory]
    [InlineData("colour:208", "\u001b[38;5;208m", "\u001b[93m")]
    [InlineData("color: 110", "\u001b[38;5;110m", "\u001b[37m")]
    [InlineData("colour:3", "\u001b[38;5;3m", "\u001b[33m")]
    public void A_256_index_parses_and_keeps_its_entry(
        string value,
        string eightBit,
        string sixteen
    )
    {
        Assert.True(ThemeColor.TryParse(value, out ThemeColor color, out string? error));
        Assert.Null(error);

        Assert.Equal(eightBit, color.Sgr(ColorSystemSupport.TrueColor));
        Assert.Equal(eightBit, color.Sgr(ColorSystemSupport.EightBit));
        Assert.Equal(sixteen, color.Sgr(ColorSystemSupport.Standard));
    }

    [Fact]
    public void A_hex_value_renders_as_rgb_markup_and_downgrades_at_the_console()
    {
        Assert.True(ThemeColor.TryParse("#ff8800", out ThemeColor color, out _));

        // The console owns the downgrade for scrollback markup: the renderer gives it RGB, and an
        // eight-bit console paints the nearest xterm entry rather than a raw hex escape.
        Assert.Equal("#ff8800", color.MarkupColor(ColorSystemSupport.EightBit));
        Assert.Equal("yellow", color.MarkupColor(ColorSystemSupport.Standard));
        Assert.Null(color.MarkupColor(ColorSystemSupport.NoColors));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("nope")]
    [InlineData("#ff88")]
    [InlineData("#gg0000")]
    [InlineData("colour:256")]
    [InlineData("colour:-1")]
    [InlineData("rgb:255,0,0")]
    public void An_unreadable_value_is_refused_with_the_accepted_forms(string value)
    {
        Assert.False(ThemeColor.TryParse(value, out _, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
