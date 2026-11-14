namespace Lunate.Tui;

/// <summary>
/// The semantic tones a surface paints with. A widget names the token, never a colour: the theme
/// maps each token to one, so a new widget inherits every theme and a theme survives a new widget
/// (the reason the file maps tokens instead of widgets).
/// </summary>
internal enum ThemeToken
{
    /// <summary>The product's own voice: selection, the tool name, the bash marker.</summary>
    Accent,

    /// <summary>Secondary text: comments, the detail block, the status line.</summary>
    Muted,

    /// <summary>A failed tool call, an error line.</summary>
    Error,

    /// <summary>A completed tool call.</summary>
    Success,

    /// <summary>An approval headline and the tones that ask for attention.</summary>
    Warning,

    /// <summary>Inline code and a fenced block without a grammar.</summary>
    Code,

    /// <summary>An inserted diff line.</summary>
    DiffAdd,

    /// <summary>A deleted diff line.</summary>
    DiffRemove,

    /// <summary>The prompt marker and the committed user echo.</summary>
    Prompt,

    /// <summary>A notice and the thinking block: information the session volunteers.</summary>
    Notice,
}

/// <summary>
/// The token catalogue: the ten names a theme file may set, the exact spellings
/// <c>theme.json</c> and its error messages use. Parsing is case-sensitive — a token is a file
/// key, not user prose — so a near miss is reported instead of silently accepted.
/// </summary>
internal static class ThemeTokens
{
    /// <summary>Every token, in the order the docs and the error message list them.</summary>
    internal static readonly ThemeToken[] All =
    [
        ThemeToken.Accent,
        ThemeToken.Muted,
        ThemeToken.Error,
        ThemeToken.Success,
        ThemeToken.Warning,
        ThemeToken.Code,
        ThemeToken.DiffAdd,
        ThemeToken.DiffRemove,
        ThemeToken.Prompt,
        ThemeToken.Notice,
    ];

    /// <summary>The valid token names, comma-separated, for a reader of an error message.</summary>
    internal static string List { get; } = string.Join(", ", All.Select(Name));

    /// <summary>The token's exact file spelling; <c>diff.add</c> keeps its dot.</summary>
    internal static string Name(ThemeToken token) =>
        token switch
        {
            ThemeToken.Accent => "accent",
            ThemeToken.Muted => "muted",
            ThemeToken.Error => "error",
            ThemeToken.Success => "success",
            ThemeToken.Warning => "warning",
            ThemeToken.Code => "code",
            ThemeToken.DiffAdd => "diff.add",
            ThemeToken.DiffRemove => "diff.remove",
            ThemeToken.Prompt => "prompt",
            _ => "notice",
        };

    /// <summary>Parses one exact token name; an unknown name is the caller's to report.</summary>
    internal static bool TryParse(string name, out ThemeToken token)
    {
        foreach (ThemeToken candidate in All)
        {
            if (string.Equals(Name(candidate), name, StringComparison.Ordinal))
            {
                token = candidate;
                return true;
            }
        }

        token = ThemeToken.Accent;
        return false;
    }
}
