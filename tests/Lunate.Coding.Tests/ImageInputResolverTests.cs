using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

public sealed class ImageInputResolverTests
{
    private static readonly ModelInfo VisionModel = new(
        "vision-test",
        "openai",
        null,
        128_000,
        true
    )
    {
        SupportsVision = true,
    };

    private static readonly ModelInfo TextModel = new("text-test", "openai", null, 128_000, true);

    [Fact]
    public async Task A_referenced_image_attaches_with_the_placeholder()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("diagram.png"), ImageSamples.Png(1024, 768));
        var resolver = new ImageInputResolver(temp.Root, VisionModel);

        ImageInputResult result = await ResolveAsync(resolver, "explain @diagram.png");

        Assert.Equal(ImageInputOutcome.Ready, result.Outcome);
        Assert.Equal("explain [image 1024x768]", result.Text);
        ImageAttachment image = Assert.Single(result.Images!);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(1024, image.Width);
        Assert.Equal(768, image.Height);
    }

    [Fact]
    public async Task A_quoted_reference_carries_spaces()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.File("screens"));
        File.WriteAllBytes(temp.File("screens/login page.png"), ImageSamples.Png(10, 20));
        var resolver = new ImageInputResolver(temp.Root, VisionModel);

        ImageInputResult result = await ResolveAsync(
            resolver,
            "@\"screens/login page.png\" what is wrong here?"
        );

        Assert.Equal(ImageInputOutcome.Ready, result.Outcome);
        Assert.Equal("[image 10x20] what is wrong here?", result.Text);
        Assert.Single(result.Images!);
    }

    [Fact]
    public async Task A_lone_dropped_path_becomes_the_placeholder()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("shot.png"), ImageSamples.Png(8, 6));
        var resolver = new ImageInputResolver(temp.Root, VisionModel);

        ImageInputResult result = await ResolveAsync(resolver, temp.File("shot.png"));

        Assert.Equal(ImageInputOutcome.Ready, result.Outcome);
        Assert.Equal("[image 8x6]", result.Text);
        Assert.Single(result.Images!);
    }

    [Fact]
    public async Task Repeated_references_produce_one_attachment()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("a.png"), ImageSamples.Png(5, 4));
        var resolver = new ImageInputResolver(temp.Root, VisionModel);

        ImageInputResult result = await ResolveAsync(resolver, "@a.png and @./a.png");

        Assert.Equal("[image 5x4] and [image 5x4]", result.Text);
        Assert.Single(result.Images!);
    }

    [Fact]
    public async Task A_non_image_reference_stays_as_typed()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("notes.txt"), "hello");
        var resolver = new ImageInputResolver(temp.Root, VisionModel);

        ImageInputResult result = await ResolveAsync(resolver, "summarize @notes.txt");

        Assert.Equal(ImageInputOutcome.Ready, result.Outcome);
        Assert.Equal("summarize @notes.txt", result.Text);
        Assert.Null(result.Images);
    }

    [Fact]
    public async Task Magic_bytes_decide_even_with_a_text_extension()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("picture.txt"), ImageSamples.Png(2, 3));
        var resolver = new ImageInputResolver(temp.Root, VisionModel);

        ImageInputResult result = await ResolveAsync(resolver, "@picture.txt");

        Assert.Equal("[image 2x3]", result.Text);
        Assert.Single(result.Images!);
    }

    [Fact]
    public async Task A_missing_image_path_is_refused()
    {
        using var temp = new TempDirectory();
        var resolver = new ImageInputResolver(temp.Root, VisionModel);

        ImageInputResult result = await ResolveAsync(resolver, "look at @missing.png");

        Assert.Equal(ImageInputOutcome.Refused, result.Outcome);
        Assert.Contains("missing.png", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unsupported_image_file_names_the_formats()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("diagram.svg"), "<svg/>");
        var resolver = new ImageInputResolver(temp.Root, VisionModel);

        ImageInputResult result = await ResolveAsync(resolver, "@diagram.svg");

        Assert.Equal(ImageInputOutcome.Refused, result.Outcome);
        Assert.Contains("PNG, JPEG, GIF or WebP", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_oversize_image_names_the_size_and_downscaling()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("big.png"), ImageSamples.PaddedPng(10, 10, 6 * 1024 * 1024));
        var resolver = new ImageInputResolver(temp.Root, VisionModel);

        ImageInputResult result = await ResolveAsync(resolver, "@big.png");

        Assert.Equal(ImageInputOutcome.Refused, result.Outcome);
        Assert.Contains("6.0 MB", result.Error, StringComparison.Ordinal);
        Assert.Contains("5.0 MB", result.Error, StringComparison.Ordinal);
        Assert.Contains(ImageLimits.DownscalingUnavailable, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_image_over_the_side_limit_names_both_numbers()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("wide.png"), ImageSamples.Png(5000, 500));
        var resolver = new ImageInputResolver(temp.Root, VisionModel);

        ImageInputResult result = await ResolveAsync(resolver, "@wide.png");

        Assert.Equal(ImageInputOutcome.Refused, result.Outcome);
        Assert.Contains("5000", result.Error, StringComparison.Ordinal);
        Assert.Contains("4096", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Five_images_are_refused_with_the_limit()
    {
        using var temp = new TempDirectory();
        for (int index = 1; index <= 5; index++)
        {
            File.WriteAllBytes(temp.File($"shot{index}.png"), ImageSamples.Png(index, index));
        }

        var resolver = new ImageInputResolver(temp.Root, VisionModel);

        ImageInputResult result = await ResolveAsync(
            resolver,
            "@shot1.png @shot2.png @shot3.png @shot4.png @shot5.png"
        );

        Assert.Equal(ImageInputOutcome.Refused, result.Outcome);
        Assert.Contains("4", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_model_without_vision_is_refused_by_id()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("diagram.png"), ImageSamples.Png(64, 32));
        var resolver = new ImageInputResolver(temp.Root, TextModel);

        ImageInputResult result = await ResolveAsync(resolver, "@diagram.png");

        Assert.Equal(ImageInputOutcome.Refused, result.Outcome);
        Assert.Contains("text-test", result.Error, StringComparison.Ordinal);
        Assert.Contains("does not support images", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_clipboard_image_attaches()
    {
        using var temp = new TempDirectory();
        var resolver = new ImageInputResolver(
            temp.Root,
            VisionModel,
            new FakeClipboard(ImageSamples.Png(640, 480))
        );

        ImageInputResult result = await ResolveAsync(resolver, "describe @clipboard");

        Assert.Equal("describe [image 640x480]", result.Text);
        Assert.Equal("image/png", Assert.Single(result.Images!).MimeType);
    }

    [Fact]
    public async Task An_absent_clipboard_helper_is_reported_once()
    {
        using var temp = new TempDirectory();
        var resolver = new ImageInputResolver(
            temp.Root,
            VisionModel,
            FakeClipboard.Unavailable("pngpaste")
        );

        ImageInputResult first = await ResolveAsync(resolver, "@clipboard");
        ImageInputResult second = await ResolveAsync(resolver, "@clipboard");

        Assert.Equal(ImageInputOutcome.Refused, first.Outcome);
        Assert.Contains("pngpaste", first.Error, StringComparison.Ordinal);
        Assert.Equal(ImageInputOutcome.Quiet, second.Outcome);
    }

    [Fact]
    public async Task An_empty_clipboard_is_refused()
    {
        using var temp = new TempDirectory();
        var resolver = new ImageInputResolver(
            temp.Root,
            VisionModel,
            FakeClipboard.Empty("wl-paste")
        );

        ImageInputResult result = await ResolveAsync(resolver, "@clipboard");

        Assert.Equal(ImageInputOutcome.Refused, result.Outcome);
        Assert.Contains("does not contain an image", result.Error, StringComparison.Ordinal);
    }

    private static ValueTask<ImageInputResult> ResolveAsync(
        ImageInputResolver resolver,
        string text
    ) => resolver.ResolveAsync(text, TestContext.Current.CancellationToken);

    private sealed class FakeClipboard : IClipboardImageSource
    {
        private readonly byte[]? _bytes;
        private readonly ClipboardImageStatus _status;
        private readonly string _helper;

        public FakeClipboard(byte[] bytes, string helper = "pngpaste")
        {
            _bytes = bytes;
            _status = ClipboardImageStatus.Image;
            _helper = helper;
        }

        private FakeClipboard(ClipboardImageStatus status, string helper)
        {
            _status = status;
            _helper = helper;
        }

        public Task<ClipboardImageResult> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(
                _status switch
                {
                    ClipboardImageStatus.Image => ClipboardImageResult.Image(_bytes!, _helper),
                    ClipboardImageStatus.Empty => ClipboardImageResult.Empty(_helper),
                    _ => ClipboardImageResult.Unavailable(_helper),
                }
            );

        public static FakeClipboard Empty(string helper) => new(ClipboardImageStatus.Empty, helper);

        public static FakeClipboard Unavailable(string helper) =>
            new(ClipboardImageStatus.Unavailable, helper);
    }
}
