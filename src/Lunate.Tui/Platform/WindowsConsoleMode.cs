using System.Globalization;
using System.Runtime.InteropServices;

namespace Lunate.Tui.Platform;

internal static class WindowsConsoleMode
{
    private const int StandardInputHandle = -10;
    private const int StandardOutputHandle = -11;

    private const uint EnableEchoInput = 0x0004;
    private const uint EnableLineInput = 0x0002;
    private const uint EnableProcessedInput = 0x0001;
    private const uint EnableVirtualTerminalInput = 0x0200;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);

    public static bool IsConsoleAttached()
    {
        var handle = GetStdHandle(StandardInputHandle);
        return handle != IntPtr.Zero && handle != new IntPtr(-1) && GetConsoleMode(handle, out _);
    }

    public static IDisposable Enter()
    {
        var input = GetStdHandle(StandardInputHandle);
        if (!GetConsoleMode(input, out uint inputMode))
        {
            throw new InvalidOperationException(
                "the Windows console mode could not be read; interactive input is unavailable."
            );
        }

        uint rawInputMode =
            (inputMode & ~(EnableEchoInput | EnableLineInput | EnableProcessedInput))
            | EnableVirtualTerminalInput;
        if (!SetConsoleMode(input, rawInputMode))
        {
            throw new InvalidOperationException(
                ModeFailure("virtual-terminal input could not be enabled on the Windows console")
            );
        }

        var output = GetStdHandle(StandardOutputHandle);
        bool outputChanged = GetConsoleMode(output, out uint outputMode);
        if (outputChanged && !SetConsoleMode(output, outputMode | EnableVirtualTerminalProcessing))
        {
            SetConsoleMode(input, inputMode);
            throw new InvalidOperationException(
                ModeFailure("virtual-terminal output could not be enabled on the Windows console")
            );
        }

        return new Restore(input, inputMode, output, outputMode, outputChanged);
    }

    private static string ModeFailure(string message) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{message} (error {Marshal.GetLastWin32Error()}); use Windows Terminal, or re-enable pseudo-console support."
        );

    private sealed class Restore(
        IntPtr input,
        uint inputMode,
        IntPtr output,
        uint outputMode,
        bool outputChanged
    ) : IDisposable
    {
        // A failed restore leaves the user's terminal in raw mode, so Dispose surfaces
        // the failure instead of swallowing it.
        public void Dispose()
        {
            if (!SetConsoleMode(input, inputMode))
            {
                throw new InvalidOperationException(
                    ModeFailure("the Windows console input mode could not be restored")
                );
            }

            if (outputChanged && !SetConsoleMode(output, outputMode))
            {
                throw new InvalidOperationException(
                    ModeFailure("the Windows console output mode could not be restored")
                );
            }
        }
    }
}
