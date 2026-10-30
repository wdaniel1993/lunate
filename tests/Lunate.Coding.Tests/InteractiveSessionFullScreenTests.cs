using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The full-screen mode behind its own switch: <c>--fullscreen</c> and <c>tui.fullscreen</c> opt in,
/// the inline session stays the default, the mode enters the alternate screen after the first frame
/// and leaves it on every exit path with the same quit keys the inline session uses — no new key,
/// no leftover escape state.
/// </summary>
public sealed class InteractiveSessionFullScreenTests
{
    private static LaunchOptions Launch(bool fullscreen = true) =>
        fullscreen ? new LaunchOptions { Fullscreen = true } : new LaunchOptions();

    [Fact]
    public async Task The_default_session_never_enters_the_alternate_screen()
    {
        using var host = new InteractiveSessionHost(launch: Launch(fullscreen: false));
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Advance(33);

        // The inline session paints normal terminal output for the whole run.
        Assert.Equal(0, host.Console.AlternateScreenEntries);

        // Quit with the same key that leaves the inline session today.
        host.Console.SendCtrlD();
        await WaitFirstCtrlDAsync(host);
        host.Console.SendCtrlD();
        await run;

        string writes = string.Concat(host.Console.Writes);
        Assert.Equal(0, CountAll(writes, AlternateScreen.EnterSequence));
        Assert.Equal(0, CountAll(writes, AlternateScreen.LeaveSequence));
    }

    [Fact]
    public async Task Full_screen_mode_enters_after_the_first_frame_and_leaves_on_quit()
    {
        using var host = new InteractiveSessionHost(launch: Launch());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Advance(33);

        // The enter sequence came after the first frame the live area painted, and the
        // session paints on the alternate screen from here on.
        Assert.True(host.Console.InAlternateScreen);
        Assert.Equal(1, host.Console.AlternateScreenEntries);
        string writes = string.Concat(host.Console.Writes);
        int enter = writes.IndexOf(AlternateScreen.EnterSequence, StringComparison.Ordinal);
        Assert.True(enter > 0, "the enter sequence must not be the first write");
        Assert.Equal(0, CountAll(writes, AlternateScreen.LeaveSequence));

        host.Console.SendCtrlD();
        await WaitFirstCtrlDAsync(host);
        host.Console.SendCtrlD();
        await run;

        Assert.False(host.Console.InAlternateScreen);
        writes = string.Concat(host.Console.Writes);
        Assert.Equal(1, CountAll(writes, AlternateScreen.EnterSequence));
        Assert.Equal(1, CountAll(writes, AlternateScreen.LeaveSequence));
        Assert.Equal(1, host.Console.AlternateScreenEntries);
        // The leave is the last thing the session wrote: no leftover escape state.
        Assert.EndsWith(AlternateScreen.LeaveSequence, writes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_tui_fullscreen_setting_opts_in_the_same_way_as_the_flag()
    {
        using var host = new InteractiveSessionHost();
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """{"schemaVersion": 2, "tui": {"fullscreen": true}}"""
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Advance(33);

        Assert.True(host.Console.InAlternateScreen);

        host.Console.SendCtrlD();
        await WaitFirstCtrlDAsync(host);
        host.Console.SendCtrlD();
        await run;

        Assert.False(host.Console.InAlternateScreen);
        Assert.Equal(1, host.Console.AlternateScreenEntries);
    }

    [Fact]
    public async Task A_terminal_that_cannot_enter_keeps_the_session_usable_inline()
    {
        using var host = new InteractiveSessionHost(launch: Launch());
        host.Console.FailAlternateScreen = true;
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Advance(33);

        // The enter failed; the session stays inline and reports it like any terminal failure.
        Assert.False(host.Console.InAlternateScreen);
        string writes = string.Concat(host.Console.Writes);
        Assert.Equal(0, CountAll(writes, AlternateScreen.EnterSequence));

        // Still usable inline: the composer paints and the same quit keys behave as always.
        host.Console.SendText("draft stays");
        await host.WaitForFrameAsync("> draft stays");
        host.Console.SendCtrlC();
        await host.WaitForFrameAsync("Ask anything");
        host.Console.SendCtrlD();
        await WaitFirstCtrlDAsync(host);
        host.Console.SendCtrlD();
        await run;

        Assert.Equal(0, host.Console.AlternateScreenEntries);
    }

    [Fact]
    public async Task The_transcript_viewer_borrows_the_screen_and_returns_it_on_leave()
    {
        using var host = new InteractiveSessionHost(launch: Launch());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Advance(33);
        Assert.True(host.Console.InAlternateScreen);

        // Ctrl+O: the session leaves its screen first, then the viewer enters its own.
        host.Console.SendCtrlO();
        await host.WaitUntilAsync(() => host.Console.AlternateScreenEntries == 2);
        Assert.Equal(
            [
                AlternateScreen.EnterSequence,
                AlternateScreen.LeaveSequence,
                AlternateScreen.EnterSequence,
            ],
            ScreenTransitions(host.Console.Writes)
        );

        // A closing key (q) hands the screen back to the full-screen session.
        host.Console.SendText("q");
        await host.WaitUntilAsync(() => host.Console.AlternateScreenEntries == 3);

        host.Console.SendCtrlD();
        await WaitFirstCtrlDAsync(host);
        host.Console.SendCtrlD();
        await run;

        // Enter and leave are paired across the whole run.
        Assert.Equal(
            3,
            CountAll(string.Concat(host.Console.Writes), AlternateScreen.EnterSequence)
        );
        Assert.Equal(
            3,
            CountAll(string.Concat(host.Console.Writes), AlternateScreen.LeaveSequence)
        );
        Assert.False(host.Console.InAlternateScreen);
    }

    private static async Task WaitFirstCtrlDAsync(InteractiveSessionHost host) =>
        await host.WaitForFrameAsync("press Ctrl+D again to quit");

    /// <summary>The writes that switched the buffer, in order — the enter/leave pairing tape.</summary>
    private static List<string> ScreenTransitions(IReadOnlyList<string> writes)
    {
        List<string> transitions = [];
        foreach (string write in writes)
        {
            if (write.Contains(AlternateScreen.EnterSequence, StringComparison.Ordinal))
            {
                transitions.Add(AlternateScreen.EnterSequence);
            }

            if (write.Contains(AlternateScreen.LeaveSequence, StringComparison.Ordinal))
            {
                transitions.Add(AlternateScreen.LeaveSequence);
            }
        }

        return transitions;
    }

    private static int CountAll(string text, string part)
    {
        int count = 0;
        int at = text.IndexOf(part, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
