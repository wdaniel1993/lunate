namespace Lunate.Tui;

public static class ConsoleSupport
{
    public static string? Check(IConsoleIO console) =>
        Describe(console, OperatingSystem.IsWindows());

    internal static string? Describe(IConsoleIO console, bool isWindows)
    {
        if (console.IsInteractive)
        {
            return null;
        }

        return isWindows
            ? "no Windows console is attached; use Windows Terminal, or re-enable pseudo-console support"
            : "interactive mode needs a terminal; use `lunate -p`";
    }
}

internal sealed class NoopDisposable : IDisposable
{
    public static readonly NoopDisposable Instance = new();

    private NoopDisposable() { }

    public void Dispose() { }
}
