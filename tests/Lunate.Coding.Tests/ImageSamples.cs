using System.Buffers.Binary;

namespace Lunate.Coding.Tests;

/// <summary>Hand-built image headers for the magic-byte and dimension tests.</summary>
internal static class ImageSamples
{
    public static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)height);
        bytes[24] = 8;
        bytes[25] = 2;
        return bytes;
    }

    public static byte[] PaddedPng(int width, int height, int totalBytes)
    {
        var bytes = new byte[totalBytes];
        Png(width, height).CopyTo(bytes, 0);
        return bytes;
    }

    public static byte[] Jpeg(int width, int height)
    {
        var bytes = new byte[41];
        // SOI.
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        // APP0 (16 bytes of segment) so the SOF scan has a segment to skip.
        bytes[2] = 0xFF;
        bytes[3] = 0xE0;
        bytes[4] = 0x00;
        bytes[5] = 0x10;
        "JFIF\0"u8.CopyTo(bytes.AsSpan(6));
        // SOF0 at offset 20.
        bytes[20] = 0xFF;
        bytes[21] = 0xC0;
        bytes[22] = 0x00;
        bytes[23] = 0x11;
        bytes[24] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(25), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(27), (ushort)width);
        bytes[29] = 3;
        // EOI.
        bytes[39] = 0xFF;
        bytes[40] = 0xD9;
        return bytes;
    }

    public static byte[] Gif(int width, int height)
    {
        var bytes = new byte[13];
        "GIF89a"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort)height);
        return bytes;
    }

    public static byte[] WebPVp8(int width, int height)
    {
        var bytes = new byte[30];
        WriteRiffHeader(bytes, "VP8 "u8, chunkSize: 10);
        bytes[20] = 0x10;
        bytes[23] = 0x9D;
        bytes[24] = 0x01;
        bytes[25] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), (ushort)height);
        return bytes;
    }

    public static byte[] WebPVp8l(int width, int height)
    {
        var bytes = new byte[25];
        WriteRiffHeader(bytes, "VP8L"u8, chunkSize: 5);
        bytes[20] = 0x2F;
        var packed = (uint)(width - 1) | ((uint)(height - 1) << 14);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(21), packed);
        return bytes;
    }

    public static byte[] WebPVp8x(int width, int height)
    {
        var bytes = new byte[30];
        WriteRiffHeader(bytes, "VP8X"u8, chunkSize: 10);
        WriteUInt24LittleEndian(bytes.AsSpan(24), width - 1);
        WriteUInt24LittleEndian(bytes.AsSpan(27), height - 1);
        return bytes;
    }

    private static void WriteRiffHeader(byte[] bytes, ReadOnlySpan<byte> chunk, int chunkSize)
    {
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        "WEBP"u8.CopyTo(bytes.AsSpan(8));
        chunk.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)chunkSize);
    }

    private static void WriteUInt24LittleEndian(Span<byte> target, int value)
    {
        target[0] = (byte)value;
        target[1] = (byte)(value >> 8);
        target[2] = (byte)(value >> 16);
    }
}
