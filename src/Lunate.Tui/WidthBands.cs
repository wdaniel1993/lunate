namespace Lunate.Tui;

/// <summary>The terminal's three width bands, evaluated at render time from its columns.</summary>
internal enum WidthBand
{
    Compact,
    Standard,
    Full,
}

/// <summary>
/// The one source of the TUI's width breakpoints: below <see cref="NarrowWidth"/> the compact
/// rules apply, from <see cref="FullWidth"/> the full layout, and the standard band keeps the
/// full structure while its existing per-component boundaries (rules at 80, descriptions at 60)
/// continue to apply.
/// </summary>
internal static class WidthBands
{
    public const int NarrowWidth = 60;

    public const int FullWidth = 100;

    public static WidthBand Of(int width) =>
        width < NarrowWidth ? WidthBand.Compact
        : width < FullWidth ? WidthBand.Standard
        : WidthBand.Full;

    public static bool IsCompact(int width) => width < NarrowWidth;
}
