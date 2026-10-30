using System.Globalization;

namespace Lunate.Tui;

/// <summary>
/// Parses a key specification the way the help overlay spells a binding: modifiers first
/// (<c>Ctrl</c>, <c>Alt</c>, <c>Shift</c>), then one named key or a single character —
/// <c>Ctrl+J</c>, <c>Alt+Enter</c>, <c>Shift+Tab</c>, <c>Esc</c>, <c>?</c>. The canonical spelling
/// is <see cref="KeyPatternFormatter.Describe"/>, so a parsed spec round-trips through it. This is
/// the remap vocabulary; the binding itself stays declared in <c>keys.json</c>.
/// </summary>
internal static class KeyString
{
    internal const string ValidSpec =
        "modifiers Ctrl, Alt and Shift, then Enter, Tab, Space, Backspace, Delete, Insert, "
        + "Home, End, PgUp, PgDn, Up, Down, Left, Right, Esc (Escape), F1-F12, or one character";

    private static readonly string[] KindNames =
    [
        "enter",
        "tab",
        "space",
        "backspace",
        "delete",
        "ins",
        "insert",
        "home",
        "end",
        "pgup",
        "pageup",
        "pgdn",
        "pagedown",
        "up",
        "down",
        "left",
        "right",
        "esc",
        "escape",
    ];

    public static bool TryParse(string? spec, out KeyPattern pattern, out string? error)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            pattern = new KeyPattern(KeyKind.Character);
            error = "a key is required: " + ValidSpec;
            return false;
        }

        string[] parts = spec.Split('+');
        if (parts.Any(part => part.Length == 0))
        {
            pattern = new KeyPattern(KeyKind.Character);
            error = NotAKey(spec);
            return false;
        }

        bool ctrl = false;
        bool alt = false;
        bool shift = false;
        foreach (string part in parts.Take(parts.Length - 1))
        {
            string lower = part.ToLowerInvariant();
            if (lower == "ctrl" && !ctrl)
            {
                ctrl = true;
            }
            else if (lower == "alt" && !alt)
            {
                alt = true;
            }
            else if (lower == "shift" && !shift)
            {
                shift = true;
            }
            else
            {
                pattern = new KeyPattern(KeyKind.Character);
                error = NotAKey(spec);
                return false;
            }
        }

        string key = parts[^1];
        if (key.Length == 1)
        {
            string text = ctrl || alt ? key.ToLowerInvariant() : key;
            pattern = new KeyPattern(KeyKind.Character, ctrl, alt, shift, text);
            error = null;
            return true;
        }

        KeyKind? kind = NamedKind(key);
        if (kind is not null)
        {
            pattern = new KeyPattern(kind.Value, ctrl, alt, shift);
            error = null;
            return true;
        }

        pattern = new KeyPattern(KeyKind.Character);
        error = NotAKey(spec);
        return false;
    }

    private static string NotAKey(string spec) =>
        string.Create(CultureInfo.InvariantCulture, $"'{spec}' is not a key: {ValidSpec}");

    private static KeyKind? NamedKind(string name)
    {
        string lower = name.ToLowerInvariant();
        if (KindNames.Contains(lower))
        {
            return lower switch
            {
                "enter" => KeyKind.Enter,
                "tab" => KeyKind.Tab,
                "space" => KeyKind.Space,
                "backspace" => KeyKind.Backspace,
                "delete" => KeyKind.Delete,
                "ins" or "insert" => KeyKind.Insert,
                "home" => KeyKind.Home,
                "end" => KeyKind.End,
                "pgup" or "pageup" => KeyKind.PageUp,
                "pgdn" or "pagedown" => KeyKind.PageDown,
                "up" => KeyKind.Up,
                "down" => KeyKind.Down,
                "left" => KeyKind.Left,
                "right" => KeyKind.Right,
                _ => KeyKind.Escape,
            };
        }

        if (
            lower.Length is 2 or 3
            && lower[0] == 'f'
            && int.TryParse(
                lower[1..],
                System.Globalization.NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int function
            )
            && function is >= 1 and <= 12
        )
        {
            return (KeyKind)((int)KeyKind.F1 + function - 1);
        }

        return null;
    }
}
