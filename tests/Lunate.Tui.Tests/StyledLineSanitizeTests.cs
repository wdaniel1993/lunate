using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// The only boundary for committed markup: every span passes <see cref="StyledLine"/>'s Add, so a
/// hostile tool result, diff or detail cannot carry escape sequences (OSC 52, OSC 8, C1 bytes)
/// into the scrollback.
/// </summary>
public sealed class StyledLineSanitizeTests
{
    [Fact]
    public void Add_strips_control_bytes_but_keeps_the_printable_remainder()
    {
        var line = new StyledLine();
        line.Add("ok\u001b]52;c;Q2xpY2JvYXJk\u0007x\u009b2J", SpanStyle.Plain);

        Assert.Equal(
            "ok]52;c;Q2xpY2JvYXJkx2J",
            string.Concat(line.Spans.Select(span => span.Text))
        );
    }

    [Fact]
    public void Add_expands_a_tab_to_one_space_but_keeps_the_text()
    {
        var line = new StyledLine();
        line.Add("a\tb", SpanStyle.Plain);

        Assert.Equal("a b", line.ToMarkup());
    }

    [Fact]
    public void Add_drops_every_escape_byte_when_only_controls_remain()
    {
        var line = new StyledLine();
        line.Add("\u001b[2J\u001b[H", SpanStyle.Plain);

        // Only the printable remnants "[" survive; nothing the terminal paints remains.
        Assert.DoesNotContain("\u001b", line.ToMarkup(), StringComparison.Ordinal);
        Assert.Equal("[[2J[[H", line.ToMarkup());
    }

    [Fact]
    public void Add_keeps_an_embedded_newline()
    {
        // Newlines split spans before Add (TuiUserInteraction.RenderBlock); a surviving one stays.
        var line = new StyledLine();
        line.Add("a\nb", SpanStyle.Plain);

        Assert.Equal("a\nb", line.ToMarkup());
    }
}
