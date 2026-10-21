using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class ImageInputPrintModeTests
{
    [Fact]
    public async Task A_referenced_image_reaches_the_request_as_data()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("diagram.png"), ImageSamples.Png(1024, 768));
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "explain @diagram.png",
        };

        (int exitCode, _, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        IReadOnlyList<ChatMessage> request = Assert.Single(client.Requests);
        ChatMessage user = Assert.Single(request, message => message.Role == ChatRole.User);
        Assert.Contains("[image 1024x768]", user.Text, StringComparison.Ordinal);
        DataContent image = Assert.Single(user.Contents.OfType<DataContent>());
        Assert.Equal("image/png", image.MediaType);
    }

    [Fact]
    public async Task A_missing_image_path_exits_two_without_a_model_call()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.File("a.png"), ImageSamples.Png(4, 4));
        var client = new ScriptedChatClient();
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "compare @a.png with @b.png",
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(2, exitCode);
        Assert.Empty(output);
        Assert.Contains("b.png", errors, StringComparison.Ordinal);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task A_clipboard_image_reaches_the_request()
    {
        using var temp = new TempDirectory();
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "describe @clipboard",
            Clipboard = new StubClipboard(ImageSamples.Png(640, 480)),
        };

        (int exitCode, _, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Empty(errors);
        ChatMessage user = Assert.Single(
            Assert.Single(client.Requests),
            message => message.Role == ChatRole.User
        );
        Assert.Contains("[image 640x480]", user.Text, StringComparison.Ordinal);
        Assert.Single(user.Contents.OfType<DataContent>());
    }

    private sealed class StubClipboard(byte[] bytes) : IClipboardImageSource
    {
        public Task<ClipboardImageResult> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ClipboardImageResult.Image(bytes, "pngpaste"));
    }
}
