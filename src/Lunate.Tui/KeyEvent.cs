namespace Lunate.Tui;

public enum KeyKind
{
    Character,
    Space,
    Enter,
    Tab,
    Backspace,
    Delete,
    Escape,
    Insert,
    Home,
    End,
    PageUp,
    PageDown,
    Up,
    Down,
    Left,
    Right,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
    Paste,
    Unknown,
}

public sealed record KeyEvent(KeyKind Kind, string? Text, bool Ctrl, bool Shift, bool Alt);
