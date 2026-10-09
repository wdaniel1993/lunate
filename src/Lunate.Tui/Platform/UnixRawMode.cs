using System.Runtime.InteropServices;

namespace Lunate.Tui.Platform;

internal static class UnixRawMode
{
    private const int StandardInputFd = 0;
    private const int ActionNow = 0;
    private const int TermiosBufferSize = 128;

    private const int LocalFlagOffset = 12;
    private const int InputFlagOffset = 0;

    private const uint Echo = 0x0000_0008;

    private static readonly bool IsMacOs = OperatingSystem.IsMacOS();

    private static uint CanonicalFlag => (uint)(IsMacOs ? 0x0000_0100 : 0x0000_0002);
    private static uint SignalFlag => (uint)(IsMacOs ? 0x0000_0080 : 0x0000_0001);
    private static uint ExtendedFlag => (uint)(IsMacOs ? 0x0000_0400 : 0x0000_8000);
    private static uint FlowControlFlag => (uint)(IsMacOs ? 0x0000_0200 : 0x0000_0400);

    private static int VMinOffset => IsMacOs ? 32 : 23;
    private static int VTimeOffset => IsMacOs ? 33 : 22;

    [DllImport("libc", SetLastError = true)]
    private static extern int tcgetattr(int fd, byte[] termios);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcsetattr(int fd, int optionalActions, byte[] termios);

    public static IDisposable Enter()
    {
        var state = new byte[TermiosBufferSize];
        if (tcgetattr(StandardInputFd, state) != 0)
        {
            throw new InvalidOperationException(
                "the terminal attributes of stdin could not be read; interactive input is unavailable."
            );
        }

        var original = (byte[])state.Clone();

        uint local = BitConverter.ToUInt32(state, LocalFlagOffset);
        local &= ~Echo & ~CanonicalFlag & ~SignalFlag & ~ExtendedFlag;
        BitConverter.TryWriteBytes(state.AsSpan(LocalFlagOffset), local);

        uint input = BitConverter.ToUInt32(state, InputFlagOffset);
        input &= ~FlowControlFlag;
        BitConverter.TryWriteBytes(state.AsSpan(InputFlagOffset), input);

        state[VMinOffset] = 1;
        state[VTimeOffset] = 0;

        if (tcsetattr(StandardInputFd, ActionNow, state) != 0)
        {
            throw new InvalidOperationException(
                "the terminal attributes of stdin could not be changed; interactive input is unavailable."
            );
        }

        return new Restore(original);
    }

    private sealed class Restore(byte[] original) : IDisposable
    {
        public void Dispose() => tcsetattr(StandardInputFd, ActionNow, original);
    }
}
