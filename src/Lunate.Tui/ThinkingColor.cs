using Spectre.Console;

namespace Lunate.Tui;

/// <summary>
/// The <c>thinking</c> colour for the raw live frame, ahead of the theme resolver: the visual
/// spec's dark 256 value (#af87d7, xterm 140) on colour-capable terminals, magenta in 16-colour
/// mode, and no SGR at all under <c>NO_COLOR</c> — the glyph and the words carry the meaning, so
/// the text never changes with the colour.
/// </summary>
internal static class ThinkingColor
{
    /// <summary>The visual spec's dark value; the theme card moves this behind its resolver.</summary>
    internal const string DarkHex = "#af87d7";

    private const string TrueColorSgr = "\u001b[38;2;175;135;215m";
    private const string EightBitSgr = "\u001b[38;5;140m";
    private const string SixteenColorSgr = "\u001b[35m";
    private const string ResetSgr = "\u001b[39m";

    /// <summary>Wraps one live-frame line in the thinking colour; plain text under NO_COLOR.</summary>
    internal static string Wrap(string line, ColorSystemSupport color) =>
        color switch
        {
            ColorSystemSupport.NoColors => line,
            ColorSystemSupport.TrueColor => TrueColorSgr + line + ResetSgr,
            ColorSystemSupport.EightBit => EightBitSgr + line + ResetSgr,
            _ => SixteenColorSgr + line + ResetSgr,
        };
}
