using Lunate.Ai;
using Lunate.Tui;

namespace Lunate.Coding;

/// <summary>
/// The session's theme: the built-in the selection names plus the token file that overlays it.
/// Selection is <c>--theme</c> — a built-in name, or the path of a file — over <c>tui.theme</c>
/// over <c>default</c>; the user file is <c>theme.json</c> in the session's home (
/// <see cref="LunateHome"/>-resolved, so <c>$LUNATE_HOME</c> moves it like every other state file).
/// A file that cannot be applied is discarded whole with one warning line and the built-in stands,
/// the same all-or-nothing rule the user keymap follows.
/// <seealso cref="InteractiveSession.ApplyTuiPresentation"/>
/// </summary>
internal sealed partial class InteractiveSession
{
    /// <summary>
    /// Resolves the theme once, at composition time, and applies it to every surface. The flag
    /// decides the base theme when it names one; when it names a path that file is the overlay,
    /// otherwise <c>theme.json</c> in the home is. Tokens the file sets replace the base's and the
    /// rest keep the base's, so a file that only changes the accent leaves the labels alone.
    /// </summary>
    private void ApplyTheme()
    {
        string? flag = _options.Launch.Theme;
        TuiTheme baseTheme = TuiTheme.Default;
        if (TuiTheme.TryGet(flag, out TuiTheme named))
        {
            baseTheme = named;
        }
        else if (TuiTheme.TryGet(_settings.Tui.Theme, out TuiTheme configured))
        {
            baseTheme = configured;
        }

        // A flag that is not a built-in name is the file this run reads instead of the default one;
        // a flag that names a built-in still lets the user's theme.json overlay it.
        string path = ThemeFile.ResolvePath(
            TuiTheme.TryGet(flag, out _) ? null : flag,
            LunateHome.In(_options.HomeDirectory)
        );
        ThemeOutcome outcome = ThemeFile.Read(path);
        if (outcome.Error is not null)
        {
            Notify(outcome.Error, NoticeLevel.Warning);
        }

        _themeOverlay = outcome.Tokens;
        SetTheme(baseTheme);
    }

    /// <summary>
    /// The capabilities a scrollback renderer resolves its tokens with: the colour plan, not the
    /// raw detection, so a hex token downgrades to the depth the console actually paints.
    /// </summary>
    private TerminalCapabilities RenderCapabilities() =>
        _capabilities with
        {
            Color = _color,
            Theme = _theme,
        };
}
