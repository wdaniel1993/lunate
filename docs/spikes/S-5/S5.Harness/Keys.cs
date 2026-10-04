namespace S5.Harness;

public static class Keys
{
    public static ConsoleKeyInfo CtrlC { get; } = new('\x03', ConsoleKey.C, shift: false, alt: false, control: true);

    public static ConsoleKeyInfo Escape { get; } = new('\x1b', ConsoleKey.Escape, shift: false, alt: false, control: false);

    public static ConsoleKeyInfo Enter { get; } = new('\r', ConsoleKey.Enter, shift: false, alt: false, control: false);

    public static ConsoleKeyInfo Backspace { get; } = new('\b', ConsoleKey.Backspace, shift: false, alt: false, control: false);

    public static ConsoleKeyInfo Letter(char value) => new(value, (ConsoleKey)char.ToUpperInvariant(value), shift: false, alt: false, control: false);

    public static ConsoleKeyInfo Space { get; } = new(' ', ConsoleKey.Spacebar, shift: false, alt: false, control: false);
}
