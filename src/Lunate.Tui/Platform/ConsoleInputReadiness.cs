using System.Runtime.InteropServices;

namespace Lunate.Tui.Platform;

/// <summary>
/// Whether stdin has bytes waiting, without consuming them: <c>poll</c> on Unix, a wait on the
/// console handle on Windows. The key pump asks first and reads only when the answer is yes, so a
/// suspended pump holds no blocking read that could steal the child's keystrokes.
/// </summary>
internal static class ConsoleInputReadiness
{
    internal const short PollIn = 0x0001;
    internal const int PollIntervalMs = 200;

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;

        public short Events;

        public short Revents;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int poll([In, Out] PollFd[] fds, uint nfds, int timeoutMs);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    private const int StandardInputHandle = -10;
    private const uint WaitObject0 = 0;

    /// <summary>True when stdin is ready; false on a timeout. Throws when the wait itself fails.</summary>
    public static bool IsReady(int timeoutMs)
    {
        if (OperatingSystem.IsWindows())
        {
            IntPtr input = GetStdHandle(StandardInputHandle);
            return WaitForSingleObject(input, (uint)timeoutMs) == WaitObject0;
        }

        PollFd[] fds = [new() { Fd = 0, Events = PollIn }];
        int ready = poll(fds, 1, timeoutMs);
        if (ready < 0)
        {
            throw new IOException("the terminal's input descriptor could not be polled.");
        }

        return ready > 0;
    }
}
