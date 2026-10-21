using System.Buffers.Binary;

namespace Lunate.Coding;

/// <summary>
/// The hand-written image sniffers behind the image paths of the tools and the input resolver.
/// Magic bytes decide the format; the header parser yields the stored pixel dimensions. Every
/// path is bounds-checked, so a malformed or truncated header is refused instead of thrown.
/// </summary>
internal static class ImageFile
{
    /// <summary>The shortest prefix that identifies every supported format.</summary>
    public const int ProbeBytes = 16;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Vp8StartCode = [0x9D, 0x01, 0x2A];

    /// <summary>Detects a supported format from its magic bytes; the extension is never consulted.</summary>
    public static bool TryDetectMime(ReadOnlySpan<byte> prefix, out string mime)
    {
        if (HasPngSignature(prefix))
        {
            mime = "image/png";
            return true;
        }

        if (prefix.Length >= 3 && prefix[0] == 0xFF && prefix[1] == 0xD8 && prefix[2] == 0xFF)
        {
            mime = "image/jpeg";
            return true;
        }

        if (
            prefix.Length >= 6
            && (prefix[..6].SequenceEqual("GIF87a"u8) || prefix[..6].SequenceEqual("GIF89a"u8))
        )
        {
            mime = "image/gif";
            return true;
        }

        if (
            prefix.Length >= 12
            && prefix[..4].SequenceEqual("RIFF"u8)
            && prefix.Slice(8, 4).SequenceEqual("WEBP"u8)
        )
        {
            mime = "image/webp";
            return true;
        }

        mime = string.Empty;
        return false;
    }

    /// <summary>
    /// Parses the stored width and height from a supported image header; false for a malformed or
    /// truncated header, never an exception.
    /// </summary>
    public static bool TryReadDimensions(
        ReadOnlySpan<byte> data,
        out string mime,
        out int width,
        out int height
    )
    {
        width = 0;
        height = 0;
        if (!TryDetectMime(data, out mime))
        {
            return false;
        }

        return mime switch
        {
            "image/png" => TryReadPng(data, out width, out height),
            "image/jpeg" => TryReadJpeg(data, out width, out height),
            "image/gif" => TryReadGif(data, out width, out height),
            "image/webp" => TryReadWebP(data, out width, out height),
            _ => false,
        };
    }

    private static bool HasPngSignature(ReadOnlySpan<byte> data) =>
        data.Length >= 8 && data[..8].SequenceEqual(PngSignature);

    private static bool TryReadPng(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 24 || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        var parsedWidth = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4));
        var parsedHeight = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4));
        if (
            parsedWidth == 0
            || parsedHeight == 0
            || parsedWidth > int.MaxValue
            || parsedHeight > int.MaxValue
        )
        {
            return false;
        }

        width = (int)parsedWidth;
        height = (int)parsedHeight;
        return true;
    }

    private static bool TryReadJpeg(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        var index = 2;
        while (index + 1 < data.Length)
        {
            if (data[index] != 0xFF)
            {
                return false;
            }

            var marker = data[index + 1];
            if (marker == 0xFF)
            {
                // Fill byte before the real marker.
                index++;
                continue;
            }

            if (IsStandaloneJpegMarker(marker))
            {
                index += 2;
                continue;
            }

            if (marker == 0x00 || index + 4 > data.Length)
            {
                return false;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index + 2, 2));
            if (length < 2)
            {
                return false;
            }

            var next = index + 2 + length;
            if (next > data.Length)
            {
                return false;
            }

            if (IsStartOfFrameJpegMarker(marker))
            {
                // length 7 covers the 2-byte length, precision, height and width.
                if (length < 7)
                {
                    return false;
                }

                var parsedHeight = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index + 5, 2));
                var parsedWidth = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index + 7, 2));
                if (parsedWidth == 0 || parsedHeight == 0)
                {
                    return false;
                }

                width = parsedWidth;
                height = parsedHeight;
                return true;
            }

            index = next;
        }

        return false;
    }

    private static bool IsStandaloneJpegMarker(byte marker) =>
        marker is 0x01 or 0xD8 or 0xD9 or (>= 0xD0 and <= 0xD7);

    private static bool IsStartOfFrameJpegMarker(byte marker) =>
        marker
            is (>= 0xC0 and <= 0xC3)
                or (>= 0xC5 and <= 0xC7)
                or (>= 0xC9 and <= 0xCB)
                or (>= 0xCD and <= 0xCF);

    private static bool TryReadGif(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 10)
        {
            return false;
        }

        var parsedWidth = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2));
        var parsedHeight = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2));
        if (parsedWidth == 0 || parsedHeight == 0)
        {
            return false;
        }

        width = parsedWidth;
        height = parsedHeight;
        return true;
    }

    private static bool TryReadWebP(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 16)
        {
            return false;
        }

        var chunk = data.Slice(12, 4);
        if (chunk.SequenceEqual("VP8 "u8))
        {
            return TryReadVp8(data, out width, out height);
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            return TryReadVp8Lossless(data, out width, out height);
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            return TryReadVp8Extended(data, out width, out height);
        }

        return false;
    }

    private static bool TryReadVp8(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 30 || !data.Slice(23, 3).SequenceEqual(Vp8StartCode))
        {
            return false;
        }

        var parsedWidth = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(26, 2)) & 0x3FFF;
        var parsedHeight = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(28, 2)) & 0x3FFF;
        if (parsedWidth == 0 || parsedHeight == 0)
        {
            return false;
        }

        width = parsedWidth;
        height = parsedHeight;
        return true;
    }

    private static bool TryReadVp8Lossless(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 25 || data[20] != 0x2F)
        {
            return false;
        }

        var packed = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(21, 4));
        width = (int)(packed & 0x3FFF) + 1;
        height = (int)((packed >> 14) & 0x3FFF) + 1;
        return true;
    }

    private static bool TryReadVp8Extended(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 30)
        {
            return false;
        }

        var parsedWidth = data[24] | (data[25] << 8) | (data[26] << 16);
        var parsedHeight = data[27] | (data[28] << 8) | (data[29] << 16);
        width = parsedWidth + 1;
        height = parsedHeight + 1;
        return true;
    }
}
