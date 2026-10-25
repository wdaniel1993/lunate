namespace Lunate.Tui.Tests;

/// <summary>
/// The terminal handover behind <c>Ctrl+G</c>: the console scope proves the ordering (quiesce,
/// cooked, child, raw, resume) and that keys typed while a child owns the terminal never reach the
/// session's read loop. A real terminal proves the handover itself; this seam proves the contract.
/// </summary>
public sealed class TerminalSuspendTests
{
    [Fact]
    public void The_scope_records_the_cooked_and_raw_modes_in_order()
    {
        using var console = new FakeConsoleIO();

        using (console.EnterRawMode())
        {
            Assert.True(console.InRawMode);

            using (console.SuspendTerminal())
            {
                // The child sees the mode the process started with, not the session's raw mode.
                Assert.False(console.InRawMode);
                Assert.True(console.IsTerminalSuspended);
            }

            Assert.True(console.InRawMode);
            Assert.False(console.IsTerminalSuspended);
        }

        Assert.Equal(["suspend:cooked", "resume:raw"], console.TerminalTransitions);
        Assert.Equal(1, console.SuspendCount);
    }

    [Fact]
    public async Task Keys_typed_at_the_child_never_reach_the_reader()
    {
        using var console = new FakeConsoleIO();

        using (console.SuspendTerminal())
        {
            console.EnqueueKey(new KeyEvent(KeyKind.Character, "y", false, false, false));
            console.EnqueueKey(new KeyEvent(KeyKind.Enter, null, false, false, false));
        }

        Assert.Equal(["y", "Enter"], console.TypedAtChild);
        Assert.Empty(console.Keys);

        var read = new List<KeyEvent>();
        await foreach (KeyEvent key in console.ReadKeysAsync(TestContext.Current.CancellationToken))
        {
            read.Add(key);
        }

        Assert.Empty(read);
    }

    [Fact]
    public void Keys_enqueued_after_resume_reach_the_reader()
    {
        using var console = new FakeConsoleIO();
        using (console.EnterRawMode())
        {
            using (console.SuspendTerminal())
            {
                console.EnqueueKey(new KeyEvent(KeyKind.Character, "x", false, false, false));
            }

            console.EnqueueKey(new KeyEvent(KeyKind.Character, "z", false, false, false));
        }

        Assert.Equal(["x"], console.TypedAtChild);
        Assert.Equal("z", Assert.Single(console.Keys).Text);
    }

    [Fact]
    public void Disposing_the_scope_twice_resumes_once()
    {
        using var console = new FakeConsoleIO();
        IDisposable scope = console.SuspendTerminal();

        scope.Dispose();
        scope.Dispose();

        Assert.Equal(["suspend:cooked", "resume:raw"], console.TerminalTransitions);
    }
}
