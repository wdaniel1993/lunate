using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lunate.Coding;

internal sealed class SystemHardlinkScanFileSystem : IHardlinkScanFileSystem
{
    private const uint OpenExisting = 3;
    private const uint FileReadAttributes = 0x80;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileShareAll = 0x7;
    private const ushort RegularFileMode = 0x8000;
    private const ushort FileTypeMask = 0xF000;

    internal static readonly SystemHardlinkScanFileSystem Instance = new();

    private SystemHardlinkScanFileSystem() { }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IReadOnlyList<string> EnumerateDirectories(string path) =>
        [.. Directory.EnumerateDirectories(path)];

    public IReadOnlyList<string> EnumerateFiles(string path) => [.. Directory.EnumerateFiles(path)];

    public bool IsDirectoryLink(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) is not 0;

    public HardlinkFileMetadata? GetFileMetadata(string path) =>
        OperatingSystem.IsWindows() ? GetWindowsMetadata(path) : GetUnixMetadata(path);

    private static HardlinkFileMetadata? GetWindowsMetadata(string path)
    {
        using SafeFileHandle handle = CreateFileW(
            path,
            FileReadAttributes,
            FileShareAll,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint,
            IntPtr.Zero
        );
        if (handle.IsInvalid)
        {
            throw MetadataError();
        }

        if (!GetFileInformationByHandle(handle, out ByHandleFileInformation information))
        {
            throw MetadataError();
        }

        if (
            (information.FileAttributes & (uint)FileAttributes.Directory) != 0
            || (information.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0
        )
        {
            return null;
        }

        ulong file = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
        return new(
            new HardlinkFileIdentity(information.VolumeSerialNumber, file),
            information.NumberOfLinks
        );
    }

    private static HardlinkFileMetadata? GetUnixMetadata(string path)
    {
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException(
                "hardlink scanning requires little-endian metadata"
            );
        }

        byte[] stat = new byte[256];
        if (LStat(path, stat) != 0)
        {
            throw MetadataError();
        }

        ulong device;
        ulong inode;
        ulong links;
        ushort mode;
        if (OperatingSystem.IsMacOS())
        {
            device = BinaryPrimitives.ReadUInt32LittleEndian(stat.AsSpan(0, 4));
            mode = BinaryPrimitives.ReadUInt16LittleEndian(stat.AsSpan(4, 2));
            links = BinaryPrimitives.ReadUInt16LittleEndian(stat.AsSpan(6, 2));
            inode = BinaryPrimitives.ReadUInt64LittleEndian(stat.AsSpan(8, 8));
        }
        else if (OperatingSystem.IsLinux())
        {
            (device, inode, links, mode) = LinuxMetadata(stat);
        }
        else
        {
            throw new PlatformNotSupportedException("hardlink metadata is unavailable on this OS");
        }

        if ((mode & FileTypeMask) != RegularFileMode)
        {
            return null;
        }

        return new(new HardlinkFileIdentity(device, inode), links);
    }

    private static (ulong Device, ulong Inode, ulong Links, ushort Mode) LinuxMetadata(byte[] stat)
    {
        if (RuntimeInformation.ProcessArchitecture is Architecture.X64)
        {
            return (
                BinaryPrimitives.ReadUInt64LittleEndian(stat.AsSpan(0, 8)),
                BinaryPrimitives.ReadUInt64LittleEndian(stat.AsSpan(8, 8)),
                BinaryPrimitives.ReadUInt64LittleEndian(stat.AsSpan(16, 8)),
                (ushort)BinaryPrimitives.ReadUInt32LittleEndian(stat.AsSpan(24, 4))
            );
        }

        if (RuntimeInformation.ProcessArchitecture is Architecture.Arm64)
        {
            return (
                BinaryPrimitives.ReadUInt64LittleEndian(stat.AsSpan(0, 8)),
                BinaryPrimitives.ReadUInt64LittleEndian(stat.AsSpan(8, 8)),
                BinaryPrimitives.ReadUInt32LittleEndian(stat.AsSpan(20, 4)),
                (ushort)BinaryPrimitives.ReadUInt32LittleEndian(stat.AsSpan(16, 4))
            );
        }

        throw new PlatformNotSupportedException(
            "Linux hardlink metadata architecture is unsupported"
        );
    }

    private static IOException MetadataError() =>
        new(
            "could not read filesystem link metadata",
            new Win32Exception(Marshal.GetLastPInvokeError())
        );

    [DllImport("libc", EntryPoint = "lstat", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int LStat(string path, [Out] byte[] stat);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle handle,
        out ByHandleFileInformation information
    );

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        internal uint FileAttributes;
        internal NativeFileTime CreationTime;
        internal NativeFileTime LastAccessTime;
        internal NativeFileTime LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        internal uint LowDateTime;
        internal uint HighDateTime;
    }
}
