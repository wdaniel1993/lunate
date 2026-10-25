using System.Runtime.InteropServices;

namespace Lunate.Tui.Platform;

internal static class UnixRawMode
{
    internal const int StandardInputFd = 0;
    internal const int ActionNow = 0;
    internal const int TermiosBufferSize = 128;

    // The buffer covers both layouts (Darwin 72 bytes, Linux 60) with slack; the
    // per-platform offsets live in TermiosLayout and are pinned by tests.
    internal static TermiosLayout Current { get; } =
        OperatingSystem.IsMacOS() ? TermiosLayout.MacOs : TermiosLayout.Linux;

    [DllImport("libc", SetLastError = true)]
    private static extern int tcgetattr(int fd, byte[] termios);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcsetattr(int fd, int optionalActions, byte[] termios);

    public static ITerminalMode Enter()
    {
        var state = new byte[TermiosBufferSize];
        if (tcgetattr(StandardInputFd, state) != 0)
        {
            throw new InvalidOperationException(
                "the terminal attributes of stdin could not be read; interactive input is unavailable."
            );
        }

        var original = (byte[])state.Clone();
        Current.ApplyRaw(state);

        if (tcsetattr(StandardInputFd, ActionNow, state) != 0)
        {
            throw new InvalidOperationException(
                "the terminal attributes of stdin could not be changed; interactive input is unavailable."
            );
        }

        return new Restore(original, state);
    }

    /// <summary>The original and the raw attribute blocks; suspending swaps one for the other.</summary>
    private sealed class Restore(byte[] original, byte[] raw) : ITerminalMode
    {
        private bool _suspended;

        public void Suspend()
        {
            if (_suspended)
            {
                return;
            }

            tcsetattr(StandardInputFd, ActionNow, original);
            _suspended = true;
        }

        public void Resume()
        {
            if (!_suspended)
            {
                return;
            }

            tcsetattr(StandardInputFd, ActionNow, raw);
            _suspended = false;
        }

        public void Dispose() => tcsetattr(StandardInputFd, ActionNow, original);
    }
}
