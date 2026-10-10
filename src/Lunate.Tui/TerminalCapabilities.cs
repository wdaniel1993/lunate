using Lunate.Tui.Platform;
using Spectre.Console;

namespace Lunate.Tui;

/// <summary>
/// The terminal capabilities detected once per session. Colour depth follows
/// <c>NO_COLOR</c>, <c>COLORTERM</c> and Spectre's detection (a non-interactive console is
/// colourless); Unicode support follows <c>TERM</c>, the Windows console and the Unix locale,
/// with <c>LUNATE_ASCII</c> / <c>LUNATE_UNICODE</c> overrides for forcing either side.
/// </summary>
public sealed record TerminalCapabilities(ColorSystemSupport Color, bool Unicode)
{
    /// <summary>
    /// Detects the capabilities for the given console context; the seams default to the real
    /// environment and the Windows console output code page, which is never probed on Unix.
    /// </summary>
    public static TerminalCapabilities Detect(
        bool isInteractive,
        bool isWindows,
        Func<string, string?>? environment = null,
        Func<int>? outputCodePage = null
    )
    {
        Func<string, string?> env = environment ?? Environment.GetEnvironmentVariable;
        return new TerminalCapabilities(
            DetectColor(isInteractive, env),
            DetectUnicode(isWindows, env, outputCodePage ?? WindowsConsoleCodePage.Output)
        );
    }

    private static ColorSystemSupport DetectColor(bool isInteractive, Func<string, string?> env)
    {
        if (!isInteractive)
        {
            return ColorSystemSupport.NoColors;
        }

        if (!string.IsNullOrEmpty(env("NO_COLOR")))
        {
            return ColorSystemSupport.NoColors;
        }

        string? colorTerm = env("COLORTERM");
        if (
            colorTerm is not null
            && (
                colorTerm.Contains("truecolor", StringComparison.OrdinalIgnoreCase)
                || colorTerm.Contains("24bit", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return ColorSystemSupport.TrueColor;
        }

        return ColorSystemSupport.Detect;
    }

    private static bool DetectUnicode(
        bool isWindows,
        Func<string, string?> env,
        Func<int> outputCodePage
    )
    {
        // The explicit overrides decide first; degrade is the safe direction, so ASCII wins.
        if (!string.IsNullOrEmpty(env("LUNATE_ASCII")))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(env("LUNATE_UNICODE")))
        {
            return true;
        }

        if (string.Equals(env("TERM"), "dumb", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (isWindows)
        {
            return !string.IsNullOrEmpty(env("WT_SESSION"))
                || outputCodePage() == WindowsConsoleCodePage.Utf8CodePage;
        }

        string? locale = FirstNonEmpty(env("LC_ALL"), env("LC_CTYPE"), env("LANG"));
        return locale is null
            || locale.Contains("utf8", StringComparison.OrdinalIgnoreCase)
            || locale.Contains("UTF-8", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return null;
    }
}
