using System.Globalization;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

public sealed class ReadToolImageTests
{
    private static readonly ToolContext Vision = new("unused", new NullAgentEvents())
    {
        Model = new ModelInfo("vision-test", "openai", null, 128_000, true)
        {
            SupportsVision = true,
        },
    };

    private static readonly ToolContext TextOnly = new("unused", new NullAgentEvents())
    {
        Model = new ModelInfo("text-test", "openai", null, 128_000, true),
    };

    [Fact]
    public async Task A_PNG_named_txt_is_read_as_an_image_with_its_exact_bytes()
    {
        using var temp = new TempDirectory();
        var bytes = ImageSamples.Png(2, 3);
        File.WriteAllBytes(temp.File("picture.txt"), bytes);

        var result = await ReadAsync(temp, """{"path":"picture.txt"}""", Vision);

        Assert.False(result.IsError, result.Output);
        Assert.Equal("[image 2x3]", result.Output);
        var image = Assert.Single(result.Images!);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(2, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal(bytes, image.Data.ToArray());
    }

    [Fact]
    public async Task A_text_file_named_png_is_still_text()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("fake.png"), "hello\n");

        var result = await ReadAsync(temp, """{"path":"fake.png"}""", Vision);

        Assert.False(result.IsError);
        Assert.Equal("     1|hello", result.Output);
        Assert.Null(result.Images);
    }

    public static TheoryData<string, byte[], string, int, int> OtherFormats =>
        new()
        {
            { "jpeg", ImageSamples.Jpeg(7, 9), "image/jpeg", 7, 9 },
            { "gif", ImageSamples.Gif(4, 6), "image/gif", 4, 6 },
            { "webp", ImageSamples.WebPVp8l(3, 5), "image/webp", 3, 5 },
        };

    [Theory]
    [MemberData(nameof(OtherFormats))]
    public async Task Every_supported_format_returns_its_mime_and_placeholder(
        string format,
        byte[] bytes,
        string mime,
        int width,
        int height
    )
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("picture.bin"), bytes);

        var result = await ReadAsync(temp, """{"path":"picture.bin"}""", Vision);

        Assert.False(result.IsError, $"{format}: {result.Output}");
        Assert.Equal(
            string.Create(CultureInfo.InvariantCulture, $"[image {width}x{height}]"),
            result.Output
        );
        var image = Assert.Single(result.Images!);
        Assert.Equal(mime, image.MimeType);
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
    }

    [Fact]
    public async Task A_model_without_vision_refuses_the_image_and_names_the_model()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("picture.png"), ImageSamples.Png(2, 3));

        var result = await ReadAsync(temp, """{"path":"picture.png"}""", TextOnly);

        Assert.True(result.IsError);
        Assert.Contains("text-test", result.Output, StringComparison.Ordinal);
        Assert.Contains("does not support images", result.Output, StringComparison.Ordinal);
        Assert.Null(result.Images);
    }

    [Fact]
    public async Task An_unknown_model_refuses_the_image_without_throwing()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("picture.png"), ImageSamples.Png(2, 3));

        var result = await ReadAsync(
            temp,
            """{"path":"picture.png"}""",
            new ToolContext("unused", new NullAgentEvents())
        );

        Assert.True(result.IsError);
        Assert.Contains("unknown does not support images", result.Output, StringComparison.Ordinal);
        Assert.Null(result.Images);
    }

    [Fact]
    public async Task An_oversize_image_names_its_size_the_limit_and_downscaling()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("picture.png"), ImageSamples.PaddedPng(2, 3, 6 * 1024 * 1024));

        var result = await ReadAsync(temp, """{"path":"picture.png"}""", Vision);

        Assert.True(result.IsError);
        Assert.Contains("6.0 MB", result.Output, StringComparison.Ordinal);
        Assert.Contains("5.0 MB", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "downscaling is not available yet",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.Contains("PNG, JPEG, GIF or WebP", result.Output, StringComparison.Ordinal);
        Assert.Null(result.Images);
    }

    [Fact]
    public async Task An_image_over_the_side_limit_names_the_dimensions_and_the_limit()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("picture.png"), ImageSamples.Png(5000, 500));

        var result = await ReadAsync(temp, """{"path":"picture.png"}""", Vision);

        Assert.True(result.IsError);
        Assert.Contains("5000x500", result.Output, StringComparison.Ordinal);
        Assert.Contains("4096", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "downscaling is not available yet",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.Null(result.Images);
    }

    [Theory]
    [InlineData("""{"path":"picture.png","offset":2}""", "offset does not apply to images")]
    [InlineData("""{"path":"picture.png","limit":2}""", "limit does not apply to images")]
    [InlineData(
        """{"path":"picture.png","offset":2,"limit":2}""",
        "offset and limit do not apply to images"
    )]
    public async Task Offset_and_limit_are_refused_for_images(string arguments, string refusal)
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("picture.png"), ImageSamples.Png(2, 3));

        var result = await ReadAsync(temp, arguments, Vision);

        Assert.True(result.IsError);
        Assert.Contains(refusal, result.Output, StringComparison.Ordinal);
        Assert.Contains("an image and has no lines", result.Output, StringComparison.Ordinal);
        Assert.Null(result.Images);
    }

    [Fact]
    public async Task A_truncated_image_header_keeps_the_binary_error()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("picture.png"), ImageSamples.Png(2, 3)[..10]);

        var result = await ReadAsync(temp, """{"path":"picture.png"}""", Vision);

        Assert.True(result.IsError);
        Assert.Equal(
            "picture.png is a binary file (10 bytes); read handles text files",
            result.Output
        );
        Assert.Null(result.Images);
    }

    private static Task<ToolResult> ReadAsync(
        TempDirectory temp,
        string arguments,
        ToolContext context
    ) => ReadAsync(temp.Root, arguments, context);

    private static Task<ToolResult> ReadAsync(string root, string arguments, ToolContext context)
    {
        var tool = new ReadTool(new Workspace(root));
        var args = JsonDocument.Parse(arguments).RootElement.Clone();

        return tool.ExecuteAsync(args, context, CancellationToken.None);
    }
}
