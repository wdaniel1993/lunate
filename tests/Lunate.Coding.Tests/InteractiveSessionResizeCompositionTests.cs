using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The production-composition guard for narrow terminals: the real host starts at 40 columns,
/// commits a short tool header and the compact footer, widens to 120 for the full layout and a
/// full header, then resizes to the standard 60-column band, which keeps the full header but
/// drops the box rules and the compact footer form.
/// </summary>
public sealed class InteractiveSessionResizeCompositionTests
{
    [Fact]
    public async Task Resizing_the_host_rerenders_each_width_band()
    {
        using var host = new InteractiveSessionHost();
        host.Console.Size = new ConsoleSize(40, 20);
        File.WriteAllText(host.Temp.File("notes-a.txt"), FileLines(13));
        File.WriteAllText(host.Temp.File("notes-b.txt"), FileLines(5));
        File.WriteAllText(host.Temp.File("notes-c.txt"), FileLines(7));
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await RunReadAsync(host, "call-1", "notes-a.txt", "first");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "gpt-4o-mini · 0%");
        Assert.Contains("✓ read · 13 lines", host.ScrollbackText, StringComparison.Ordinal);
        Assert.DoesNotContain("notes-a.txt", host.ScrollbackText, StringComparison.Ordinal);
        Assert.DoesNotContain("─", host.LastFrame, StringComparison.Ordinal);

        host.Console.PushResize(new ConsoleSize(120, 40));
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains(new string('─', 120), StringComparison.Ordinal)
                && host.LastFrame.Contains("0/128.0k", StringComparison.Ordinal);
        });
        await RunReadAsync(host, "call-2", "notes-b.txt", "second");
        Assert.Contains(
            "✓ read  notes-b.txt (5 lines)",
            host.ScrollbackText,
            StringComparison.Ordinal
        );

        host.Console.PushResize(new ConsoleSize(60, 30));
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("0/128.0k", StringComparison.Ordinal)
                && !host.LastFrame.Contains("─", StringComparison.Ordinal);
        });
        await RunReadAsync(host, "call-3", "notes-c.txt", "third");
        Assert.Contains(
            "✓ read  notes-c.txt (7 lines)",
            host.ScrollbackText,
            StringComparison.Ordinal
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private static async Task RunReadAsync(
        InteractiveSessionHost host,
        string callId,
        string file,
        string final
    )
    {
        host.Client.Enqueue(
            Scripts.Call(callId, "read", Scripts.Args(("path", file))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text(final), Scripts.Stop());
        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
            !host.Session.IsRunning && host.ScrollbackText.Contains(final, StringComparison.Ordinal)
        );
    }

    private static string FileLines(int count) =>
        string.Join('\n', Enumerable.Range(1, count).Select(n => $"line {n:00} of the file"));
}
