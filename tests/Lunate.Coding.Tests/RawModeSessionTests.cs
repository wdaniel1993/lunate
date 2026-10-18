namespace Lunate.Coding.Tests;

/// <summary>
/// The session's raw-mode lifecycle (tui capability: "enter raw mode through platform APIs").
/// The call was missing entirely, so a canonical terminal delivered Enter as LF, the VT decoder
/// read it as Ctrl+J and a prompt could never be submitted from a real terminal — invisible to
/// the suite because the scripted console injects KeyEvents below the byte layer.
/// </summary>
public sealed class RawModeSessionTests
{
    [Fact]
    public async Task The_session_enters_raw_mode_for_its_lifetime_and_leaves_it_on_exit()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        await host.WaitUntilAsync(() => host.Session.IsComposed);

        Assert.Equal(1, host.Console.RawModeEntries);
        Assert.True(host.Console.InRawMode);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;

        Assert.False(host.Console.InRawMode);
    }

    [Fact]
    public async Task A_terminal_that_cannot_switch_modes_fails_soft()
    {
        using var host = new InteractiveSessionHost();
        host.Console.FailRawMode = true;

        await host.RunAsync();

        // The platform's actionable message reaches the console; the session never composes
        // and nothing throws.
        Assert.Contains(
            "terminal attributes",
            string.Concat(host.Console.Writes),
            StringComparison.Ordinal
        );
        Assert.False(host.Session.IsComposed);
    }
}
