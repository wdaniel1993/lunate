using System.Buffers.Binary;

namespace Lunate.Tui.Platform;

// Byte layout and flag values of `struct termios` for one Unix platform. The two
// tables are pinned by UnixRawModeTests; the P/Invoke path itself can only run on a
// real terminal, which T-33's manual matrix covers (see the design deviations).
//
// Sources (fetched 2026-10-09):
//   Darwin — XNU bsd/sys/termios.h: `typedef unsigned long tcflag_t` (8 bytes on
//     LP64), `cc_t c_cc[NCCS]` with NCCS 20, VMIN 16, VTIME 17. Four 8-byte flags
//     put c_lflag at 24 and c_cc at 32, so VMIN/VTIME sit at 48/49.
//   Linux — glibc sysdeps/unix/sysv/linux/bits/termios-struct.h: `tcflag_t` is a
//     4-byte `unsigned int` and a one-byte `cc_t c_line` sits between c_lflag and
//     c_cc[NCCS = 32], so c_cc starts at 17. bits/termios-c_cc.h gives VMIN 6,
//     VTIME 5, i.e. offsets 23/22. Flag values from bits/termios-c_iflag.h and
//     bits/termios-c_lflag.h.
internal sealed record TermiosLayout(
    int IFlagOffset,
    int OFlagOffset,
    int CFlagOffset,
    int LFlagOffset,
    int CcBase,
    int CcCount,
    int VMinIndex,
    int VTimeIndex,
    uint Echo,
    uint ICanon,
    uint ISig,
    uint IExten,
    uint IFlagClearMask
)
{
    internal int VMinOffset => CcBase + VMinIndex;

    internal int VTimeOffset => CcBase + VTimeIndex;

    internal static TermiosLayout MacOs { get; } =
        new(
            IFlagOffset: 0,
            OFlagOffset: 8,
            CFlagOffset: 16,
            LFlagOffset: 24,
            CcBase: 32,
            CcCount: 20,
            VMinIndex: 16,
            VTimeIndex: 17,
            Echo: 0x0000_0008,
            ICanon: 0x0000_0100,
            ISig: 0x0000_0080,
            IExten: 0x0000_0400,
            // IGNBRK | BRKINT | PARMRK | ISTRIP | INLCR | IGNCR | ICRNL | IXON.
            IFlagClearMask: 0x0000_03EB
        );

    internal static TermiosLayout Linux { get; } =
        new(
            IFlagOffset: 0,
            OFlagOffset: 4,
            CFlagOffset: 8,
            LFlagOffset: 12,
            CcBase: 17,
            CcCount: 32,
            VMinIndex: 6,
            VTimeIndex: 5,
            Echo: 0x0000_0008,
            ICanon: 0x0000_0002,
            ISig: 0x0000_0001,
            IExten: 0x0000_8000,
            // IGNBRK | BRKINT | PARMRK | ISTRIP | INLCR | IGNCR | ICRNL | IXON.
            IFlagClearMask: 0x0000_05EB
        );

    // cfmakeraw's input half: echo, canonical mode, signals and extended processing
    // off; the full input translation set off (ICRNL included, so Enter stays CR and
    // the decoder's 0x0D/0x0A distinction holds); VMIN 1, VTIME 0.
    internal void ApplyRaw(Span<byte> state)
    {
        uint lflag = BinaryPrimitives.ReadUInt32LittleEndian(state[LFlagOffset..]);
        lflag &= ~(Echo | ICanon | ISig | IExten);
        BinaryPrimitives.WriteUInt32LittleEndian(state[LFlagOffset..], lflag);

        uint iflag = BinaryPrimitives.ReadUInt32LittleEndian(state[IFlagOffset..]);
        iflag &= ~IFlagClearMask;
        BinaryPrimitives.WriteUInt32LittleEndian(state[IFlagOffset..], iflag);

        state[VMinOffset] = 1;
        state[VTimeOffset] = 0;
    }
}
