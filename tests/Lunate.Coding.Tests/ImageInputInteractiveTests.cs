using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class ImageInputInteractiveTests
{
    [Fact]
    public async Task A_pasted_image_path_becomes_an_attachment()
    {
        using var host = new InteractiveSessionHost();
        File.WriteAllBytes(host.Temp.File("shot.png"), ImageSamples.Png(2, 3));
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText(host.Temp.File("shot.png"));
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        Assert.Contains("[image 2x3]", host.ScrollbackText, StringComparison.Ordinal);
        ChatMessage user = host.Client.Requests[0][^1];
        Assert.Contains(user.Contents, content => content is DataContent);
        Assert.Contains(
            host.Session.Transcript.OfType<TranscriptUserEntry>(),
            entry => entry.Text == "[image 2x3]"
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task An_absent_clipboard_helper_shows_one_notice_and_starts_no_run()
    {
        using var host = new InteractiveSessionHost(clipboard: UnavailableClipboard.Instance);
        Task run = host.RunAsync();

        host.Console.SendText("@clipboard");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("pngpaste", StringComparison.Ordinal);
        });

        Assert.Empty(host.Client.Requests);
        Assert.DoesNotContain("[image", host.ScrollbackText, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    private sealed class UnavailableClipboard : IClipboardImageSource
    {
        public static UnavailableClipboard Instance { get; } = new();

        public Task<ClipboardImageResult> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ClipboardImageResult.Unavailable("pngpaste"));
    }
}
