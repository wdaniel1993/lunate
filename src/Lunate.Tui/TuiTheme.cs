using Spectre.Console;

namespace Lunate.Tui;

/// <summary>
/// The named themes the <c>/settings</c> surface chooses from. A theme is a colour-depth policy
/// applied over the terminal's detected capability, reusing the capability machinery rather than
/// standing up a second colour system: <c>default</c> keeps the detection, <c>ansi16</c> pins the
/// high-contrast sixteen-colour system, <c>mono</c> drops every attribute. <c>NO_COLOR</c> wins
/// over any theme: a plan can only lower the colour depth, never raise it back.
/// </summary>
internal sealed record TuiTheme(string Name)
{
    internal const string Literals = "'default', 'ansi16' or 'mono'";

    internal static readonly string[] Names = ["default", "ansi16", "mono"];

    internal static TuiTheme Default { get; } = new("default");

    internal static TuiTheme Ansi16 { get; } = new("ansi16");

    internal static TuiTheme Mono { get; } = new("mono");

    /// <summary>Parses the exact theme literal; the error message lists <see cref="Literals"/>.</summary>
    internal static bool TryGet(string? name, out TuiTheme theme)
    {
        theme = Default;
        if (string.Equals(name, Default.Name, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(name, Ansi16.Name, StringComparison.Ordinal))
        {
            theme = Ansi16;
            return true;
        }

        if (string.Equals(name, Mono.Name, StringComparison.Ordinal))
        {
            theme = Mono;
            return true;
        }

        return false;
    }

    /// <summary>The resolved theme for a literal that must be valid; settings parsing validates first.</summary>
    internal static TuiTheme Get(string name)
    {
        TryGet(name, out TuiTheme theme);
        return theme;
    }

    /// <summary>The colour system this terminal renders with once the theme is applied.</summary>
    internal ColorSystemSupport Plan(ColorSystemSupport detected)
    {
        if (string.Equals(Name, Mono.Name, StringComparison.Ordinal))
        {
            return ColorSystemSupport.NoColors;
        }

        if (
            string.Equals(Name, Ansi16.Name, StringComparison.Ordinal)
            && detected != ColorSystemSupport.NoColors
        )
        {
            return ColorSystemSupport.Standard;
        }

        return detected;
    }
}
