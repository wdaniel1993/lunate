using Microsoft.Reactive.Testing;

namespace Lunate.Tui.Tests;

public sealed class FakeConsoleIOTests
{
    [Fact]
    public void Writes_are_recorded_in_order()
    {
        using var console = new FakeConsoleIO();

        console.Write("one");
        console.Write("two");

        Assert.Equal(["one", "two"], console.Writes);
    }

    [Fact]
    public void Raw_mode_entries_are_recorded_and_restored()
    {
        using var console = new FakeConsoleIO();

        using (console.EnterRawMode())
        {
            Assert.True(console.InRawMode);
        }

        Assert.False(console.InRawMode);
        Assert.Equal(1, console.RawModeEntries);
    }

    [Fact]
    public void Resized_skips_a_repeat_of_the_current_size()
    {
        using var console = new FakeConsoleIO();
        var seen = new List<ConsoleSize>();
        using var subscription = console.Resized.Subscribe(seen.Add);

        console.PushResize(new ConsoleSize(100, 30));
        console.PushResize(new ConsoleSize(100, 30));
        console.PushResize(new ConsoleSize(120, 40));

        Assert.Equal([new ConsoleSize(100, 30), new ConsoleSize(120, 40)], seen);
    }

    [Fact]
    public async Task ReadKeysAsync_replays_the_scripted_keys()
    {
        using var console = new FakeConsoleIO();
        console.EnqueueKeys(
            new KeyEvent(KeyKind.Character, "a", false, false, false),
            new KeyEvent(KeyKind.Enter, null, false, false, false)
        );

        var keys = new List<KeyEvent>();
        await foreach (var key in console.ReadKeysAsync(CancellationToken.None))
        {
            keys.Add(key);
        }

        Assert.Equal(2, keys.Count);
        Assert.Equal(KeyKind.Enter, keys[1].Kind);
    }

    [Fact]
    public void Size_polling_emits_only_when_the_size_changes()
    {
        var scheduler = new TestScheduler();
        var size = new ConsoleSize(80, 24);
        var seen = new List<ConsoleSize>();
        using var subscription = SystemConsoleIO
            .PollSize(() => size, TimeSpan.FromMilliseconds(250), scheduler)
            .Subscribe(seen.Add);

        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(250).Ticks);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(250).Ticks);
        size = new ConsoleSize(100, 30);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        Assert.Equal([new ConsoleSize(80, 24), new ConsoleSize(100, 30)], seen);
    }
}
