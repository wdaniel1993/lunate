using Spectre.Console;

namespace Lunate.Tui;

/// <summary>
/// A theme: a name and the ten semantic tokens mapped to colours. The three built-ins are
/// <c>default</c> (the palette the surfaces shipped with), <c>ansi16</c> (named ANSI colours only,
/// for a sixteen-colour terminal) and <c>mono</c> (no colour, emphasis by weight only). A user file
/// overlays tokens onto whichever built-in is selected; <see cref="Plan"/> still caps the colour
/// depth, so <c>NO_COLOR</c> and the detection rules win and a theme can only lower the depth,
/// never raise it back.
/// </summary>
internal sealed class TuiTheme
{
    /// <summary>The named selection a setting or a flag accepts, for an error message.</summary>
    internal const string Literals = "'default', 'ansi16' or 'mono'";

    /// <summary>The built-in theme names, in documentation order.</summary>
    internal static readonly string[] Names = ["default", "ansi16", "mono"];

    private readonly IReadOnlyDictionary<ThemeToken, ThemeColor> _tokens;

    private TuiTheme(string name, IReadOnlyDictionary<ThemeToken, ThemeColor> tokens)
    {
        Name = name;
        _tokens = tokens;
    }

    internal string Name { get; }

    /// <summary>The palette the surfaces shipped with, now behind the tokens.</summary>
    internal static TuiTheme Default { get; } = Build("default", SixteenColourDefaults());

    /// <summary>Named ANSI colours only: the selection for a sixteen-colour terminal.</summary>
    internal static TuiTheme Ansi16 { get; } =
        Build(
            "ansi16",
            new Dictionary<ThemeToken, ThemeColor>
            {
                [ThemeToken.Accent] = ThemeColor.Named(6),
                [ThemeToken.Muted] = ThemeColor.Named(8),
                [ThemeToken.Error] = ThemeColor.Named(9),
                [ThemeToken.Success] = ThemeColor.Named(2),
                [ThemeToken.Warning] = ThemeColor.Named(11),
                [ThemeToken.Code] = ThemeColor.Named(14),
                [ThemeToken.DiffAdd] = ThemeColor.Named(2),
                [ThemeToken.DiffRemove] = ThemeColor.Named(9),
                [ThemeToken.Prompt] = ThemeColor.Named(4),
                [ThemeToken.Notice] = ThemeColor.Named(5),
            }
        );

    /// <summary>No colour at all: <see cref="Plan"/> resolves every colour system to none.</summary>
    internal static TuiTheme Mono { get; } = Build("mono", SixteenColourDefaults());

    /// <summary>Parses the exact built-in name; a path is not a theme name.</summary>
    internal static bool TryGet(string? name, out TuiTheme theme)
    {
        foreach (TuiTheme candidate in new[] { Default, Ansi16, Mono })
        {
            if (string.Equals(name, candidate.Name, StringComparison.Ordinal))
            {
                theme = candidate;
                return true;
            }
        }

        theme = Default;
        return false;
    }

    /// <summary>The built-in for a name the settings store has already validated.</summary>
    internal static TuiTheme Get(string name)
    {
        TryGet(name, out TuiTheme theme);
        return theme;
    }

    /// <summary>This theme with a user file's tokens replacing the ones it names.</summary>
    internal TuiTheme WithTokens(IReadOnlyDictionary<ThemeToken, ThemeColor> overlay)
    {
        if (overlay.Count == 0)
        {
            return this;
        }

        var merged = new Dictionary<ThemeToken, ThemeColor>(_tokens);
        foreach ((ThemeToken token, ThemeColor color) in overlay)
        {
            merged[token] = color;
        }

        return new TuiTheme(Name, merged);
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

    /// <summary>One token as a scrollback span; a plain span where the colour depth allows none.</summary>
    internal SpanStyle Span(ThemeToken token, ColorSystemSupport color) =>
        new(Color: ColorOf(token).MarkupColor(color));

    /// <summary>
    /// One token around text the live frame has already laid out: raw SGR, so escape bytes never
    /// count as columns. The text alone when the colour depth allows no colour.
    /// </summary>
    internal string Wrap(ThemeToken token, string text, ColorSystemSupport color) =>
        ColorOf(token).Wrap(text, color);

    private ThemeColor ColorOf(ThemeToken token) => _tokens[token];

    private static TuiTheme Build(
        string name,
        IReadOnlyDictionary<ThemeToken, ThemeColor> tokens
    ) => new(name, tokens);

    /// <summary>
    /// The default palette, hex where the visual spec named a value and an ANSI colour elsewhere,
    /// each with the fallback the surface already used on a sixteen-colour terminal.
    /// </summary>
    private static Dictionary<ThemeToken, ThemeColor> SixteenColourDefaults() =>
        new()
        {
            // The visual spec's dark accent, muted, user and warning values.
            [ThemeToken.Accent] = new ThemeColor(0x5f, 0xd7, 0xd7, Sixteen: 6),
            [ThemeToken.Muted] = new ThemeColor(0x8a, 0x8a, 0x8a, Sixteen: 8),
            [ThemeToken.Prompt] = new ThemeColor(0x87, 0xaf, 0xd7, Sixteen: 4),
            [ThemeToken.Warning] = new ThemeColor(0xff, 0xff, 0x00, Sixteen: 11),
            // The thinking tone, which is also what a notice reads as.
            [ThemeToken.Notice] = new ThemeColor(0xaf, 0x87, 0xd7, Sixteen: 5),
            // The named colours the scrollback has always used for status and syntax.
            [ThemeToken.Error] = ThemeColor.Named(9),
            [ThemeToken.Success] = ThemeColor.Named(2),
            [ThemeToken.Code] = ThemeColor.Named(14),
            [ThemeToken.DiffAdd] = ThemeColor.Named(2),
            [ThemeToken.DiffRemove] = ThemeColor.Named(9),
        };
}
