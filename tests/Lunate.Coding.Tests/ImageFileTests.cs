namespace Lunate.Coding.Tests;

public sealed class ImageFileTests
{
    public static TheoryData<string, byte[], string, int, int> ValidImages =>
        new()
        {
            { "png", ImageSamples.Png(2, 3), "image/png", 2, 3 },
            { "jpeg", ImageSamples.Jpeg(7, 9), "image/jpeg", 7, 9 },
            { "gif", ImageSamples.Gif(4, 6), "image/gif", 4, 6 },
            { "webp-vp8", ImageSamples.WebPVp8(5, 7), "image/webp", 5, 7 },
            { "webp-vp8l", ImageSamples.WebPVp8l(3, 5), "image/webp", 3, 5 },
            { "webp-vp8x", ImageSamples.WebPVp8x(10, 12), "image/webp", 10, 12 },
        };

    public static TheoryData<string, byte[], string> MagicImages =>
        new()
        {
            { "png", ImageSamples.Png(2, 3), "image/png" },
            { "jpeg", ImageSamples.Jpeg(7, 9), "image/jpeg" },
            { "gif", ImageSamples.Gif(4, 6), "image/gif" },
            { "webp-vp8", ImageSamples.WebPVp8(5, 7), "image/webp" },
            { "webp-vp8l", ImageSamples.WebPVp8l(3, 5), "image/webp" },
            { "webp-vp8x", ImageSamples.WebPVp8x(10, 12), "image/webp" },
        };

    [Theory]
    [MemberData(nameof(ValidImages))]
    public void The_header_yields_the_mime_and_dimensions(
        string format,
        byte[] bytes,
        string mime,
        int width,
        int height
    )
    {
        Assert.True(
            ImageFile.TryReadDimensions(
                bytes,
                out var detectedMime,
                out var detectedWidth,
                out var detectedHeight
            ),
            format
        );
        Assert.Equal(mime, detectedMime);
        Assert.Equal(width, detectedWidth);
        Assert.Equal(height, detectedHeight);
    }

    [Theory]
    [MemberData(nameof(MagicImages))]
    public void The_magic_prefix_identifies_the_format(string format, byte[] bytes, string mime)
    {
        Assert.True(
            ImageFile.TryDetectMime(
                bytes.AsSpan(0, Math.Min(bytes.Length, ImageFile.ProbeBytes)),
                out var detectedMime
            ),
            format
        );
        Assert.Equal(mime, detectedMime);
    }

    [Fact]
    public void A_PNG_signature_without_IHDR_is_not_an_image()
    {
        var bytes = new byte[ImageFile.ProbeBytes];
        ImageSamples.Png(2, 3).AsSpan(0, 8).CopyTo(bytes);

        Assert.False(ImageFile.TryReadDimensions(bytes, out _, out _, out _));
    }

    [Theory]
    [MemberData(nameof(ValidImages))]
    public void Every_truncated_prefix_is_parsed_without_throwing(
        string format,
        byte[] bytes,
        string mime,
        int width,
        int height
    )
    {
        for (var length = 0; length < bytes.Length; length++)
        {
            if (
                ImageFile.TryReadDimensions(
                    bytes.AsSpan(0, length),
                    out var detectedMime,
                    out var detectedWidth,
                    out var detectedHeight
                )
            )
            {
                Assert.Equal(mime, detectedMime);
                Assert.Equal(width, detectedWidth);
                Assert.Equal(height, detectedHeight);
                Assert.True(detectedWidth > 0, format);
                Assert.True(detectedHeight > 0, format);
            }

            _ = ImageFile.TryDetectMime(bytes.AsSpan(0, length), out _);
        }
    }
}
