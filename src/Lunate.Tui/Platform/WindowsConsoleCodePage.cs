using System.Runtime.InteropServices;

namespace Lunate.Tui.Platform;

internal static class WindowsConsoleCodePage
{
    internal const int Utf8CodePage = 65001;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleOutputCP();

    /// <summary>The console output code page; 0 when no console is attached.</summary>
    public static int Output() => (int)GetConsoleOutputCP();
}
