using Spectre.Console;

namespace Lunate.Tui;

/// <summary>
/// The live frame's tones: the visual spec's dark accent (#5fd7d7, xterm 80) and muted
/// (#8a8a8a, xterm 245) values on colour-capable terminals, their 16-colour fallbacks and no
/// attribute at all under <c>NO_COLOR</c>. Raw SGR is emitted directly because the frame is
/// already composed text; width math happens before styling, so escape bytes never count as
/// columns.
/// </summary>
internal static class LivePalette
{
    /// <summary>The visual spec's dark muted value; the theme card moves this behind its resolver.</summary>
    internal const string MutedHex = "#8a8a8a";

    /// <summary>The visual spec's dark accent value.</summary>
    internal const string AccentHex = "#5fd7d7";

    /// <summary>The warning tone used by the approval headline.</summary>
    internal const string WarningHex = "#ffff00";

    private const string MutedTrueColor = "\u001b[38;2;138;138;138m";
    private const string MutedEightBit = "\u001b[38;5;245m";
    private const string MutedSixteenColor = "\u001b[90m";
    private const string AccentTrueColor = "\u001b[38;2;95;215;215m";
    private const string AccentEightBit = "\u001b[38;5;80m";
    private const string AccentSixteenColor = "\u001b[36m";
    private const string WarningTrueColor = "\u001b[38;2;255;255;0m";
    private const string WarningEightBit = "\u001b[38;5;11m";
    private const string WarningSixteenColor = "\u001b[93m";

    /// <summary>
    /// The full reset, matching what Spectre's markup emits for committed output, so the live
    /// frame and the commit end the colour the same way.
    /// </summary>
    private const string ResetSgr = "\u001b[0m";

    /// <summary>Wraps one span in the muted tone; plain text under NO_COLOR.</summary>
    internal static string Muted(string text, ColorSystemSupport color) =>
        Wrap(text, color, MutedTrueColor, MutedEightBit, MutedSixteenColor);

    /// <summary>Wraps one span in the accent tone; plain text under NO_COLOR.</summary>
    internal static string Accent(string text, ColorSystemSupport color) =>
        Wrap(text, color, AccentTrueColor, AccentEightBit, AccentSixteenColor);

    /// <summary>Wraps one span in the warning tone; plain text under NO_COLOR.</summary>
    internal static string Warning(string text, ColorSystemSupport color) =>
        Wrap(text, color, WarningTrueColor, WarningEightBit, WarningSixteenColor);

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
