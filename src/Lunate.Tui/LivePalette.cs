using Spectre.Console;

namespace Lunate.Tui;

/// <summary>
/// The live frame's muted tone: the visual spec's dark value (#8a8a8a, xterm 245) on colour-
/// capable terminals, bright black in 16-colour mode and no attribute at all under
/// <c>NO_COLOR</c>. Raw SGR is emitted directly because the frame is already composed text; the
/// width math happens before styling, so escape bytes never count as columns.
/// </summary>
internal static class LivePalette
{
    /// <summary>The visual spec's dark muted value; the theme card moves this behind its resolver.</summary>
    internal const string MutedHex = "#8a8a8a";

    private const string MutedTrueColor = "\u001b[38;2;138;138;138m";
    private const string MutedEightBit = "\u001b[38;5;245m";
    private const string MutedSixteenColor = "\u001b[90m";

    /// <summary>
    /// The full reset, matching what Spectre's markup emits for committed output, so the live
    /// frame and the commit end the colour the same way.
    /// </summary>
    private const string ResetSgr = "\u001b[0m";

    /// <summary>Wraps one span in the muted tone; plain text under NO_COLOR.</summary>
    internal static string Muted(string text, ColorSystemSupport color) =>
        Wrap(text, color, MutedTrueColor, MutedEightBit, MutedSixteenColor);

    private static string Wrap(
        string text,
        ColorSystemSupport color,
        string trueColor,
        string eightBit,
        string sixteenColor
    )
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        return color switch
        {
            ColorSystemSupport.NoColors => text,
            ColorSystemSupport.TrueColor => trueColor + text + ResetSgr,
            ColorSystemSupport.EightBit => eightBit + text + ResetSgr,
            _ => sixteenColor + text + ResetSgr,
        };
    }
}
