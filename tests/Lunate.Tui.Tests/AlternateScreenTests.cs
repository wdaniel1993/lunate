namespace Lunate.Tui.Tests;

/// <summary>
/// The alternate-screen lifecycle on the console seam: entering writes the VT sequence once,
/// the returned scope records the state while it holds the screen, and disposing it restores the
/// primary screen on every exit path — including when the body between enter and leave throws.
/// </summary>
public sealed class AlternateScreenTests
{
    [Fact]
    public void Entering_writes_the_alternate_screen_sequence_and_records_the_state()
    {
        using var console = new FakeConsoleIO();

        using IDisposable screen = console.EnterAlternateScreen();

        Assert.True(console.InAlternateScreen);
        Assert.Equal(1, console.AlternateScreenEntries);
        Assert.Equal("\u001b[?1049h", console.Writes[^1]);
    }

    [Fact]
    public void Disposing_the_scope_leaves_the_alternate_screen()
    {
        using var console = new FakeConsoleIO();
        IDisposable screen = console.EnterAlternateScreen();

        screen.Dispose();

        Assert.False(console.InAlternateScreen);
        Assert.Equal("\u001b[?1049l", console.Writes[^1]);
    }

    [Fact]
    public void An_exception_between_enter_and_leave_still_restores_the_screen()
    {
        using var console = new FakeConsoleIO();

        void Fail()
        {
            using IDisposable screen = console.EnterAlternateScreen();
            throw new InvalidOperationException("a failing paint");
        }

        Assert.Throws<InvalidOperationException>(Fail);

        Assert.False(console.InAlternateScreen);
        Assert.Equal("\u001b[?1049l", console.Writes[^1]);
    }

    [Fact]
    public void Disposing_the_scope_twice_leaves_the_screen_once()
    {
        using var console = new FakeConsoleIO();
        IDisposable screen = console.EnterAlternateScreen();

        screen.Dispose();
        screen.Dispose();

        Assert.False(console.InAlternateScreen);
        Assert.Equal(new[] { "\u001b[?1049h", "\u001b[?1049l" }, console.Writes);
    }

    [Fact]
    public void Two_scopes_track_each_enter()
    {
        using var console = new FakeConsoleIO();

        IDisposable first = console.EnterAlternateScreen();
        first.Dispose();
        IDisposable second = console.EnterAlternateScreen();
        second.Dispose();

        Assert.Equal(2, console.AlternateScreenEntries);
        Assert.False(console.InAlternateScreen);
        Assert.Equal(
            new[] { "\u001b[?1049h", "\u001b[?1049l", "\u001b[?1049h", "\u001b[?1049l" },
            console.Writes
        );
    }
}
