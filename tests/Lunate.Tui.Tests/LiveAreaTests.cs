using Microsoft.Reactive.Testing;

namespace Lunate.Tui.Tests;

public sealed class LiveAreaTests
{
    [Fact]
    public void Start_paints_the_prompt_once()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(40, 10));
        using var area = new LiveArea(console, scheduler);

        area.Start();

        Assert.Single(console.Writes);
        Assert.Contains("> ", console.Writes[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_burst_within_one_frame_window_paints_once()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(40, 10));
        using var area = new LiveArea(console, scheduler);
        area.Start();

        Type(area, "hello");
        Advance(scheduler, 33);

        Assert.Equal(2, console.Writes.Count);

        area.PostKey(Char("x"));
        Advance(scheduler, 20);

        Assert.Equal(2, console.Writes.Count);

        Advance(scheduler, 13);

        Assert.Equal(3, console.Writes.Count);
    }

    [Fact]
    public void Nothing_is_painted_before_the_sample_tick_drains_the_state()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(40, 10));
        using var area = new LiveArea(console, scheduler);
        area.Start();

        area.PostKey(Char("z"));

        Assert.Single(console.Writes);

        Advance(scheduler, 33);

        Assert.Equal(2, console.Writes.Count);
        Assert.Contains("> z", console.Writes[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Input_editing_is_reflected_in_the_next_frame()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(40, 10));
        using var area = new LiveArea(console, scheduler);
        area.Start();

        Type(area, "abc");
        area.PostKey(new KeyEvent(KeyKind.Backspace, null, false, false, false));
        Advance(scheduler, 33);

        Assert.Contains("> ab", console.Writes[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Spinner_frames_advance_on_virtual_time_only_while_a_tool_runs()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(40, 10));
        using var area = new LiveArea(console, scheduler);
        area.Start();

        Advance(scheduler, 1000);
        Assert.Single(console.Writes);

        area.SetTool("read");
        Advance(scheduler, 33);
        Assert.Contains("| read", Text(console), StringComparison.Ordinal);

        Advance(scheduler, 99);
        Assert.Contains("/ read", console.Writes[^1], StringComparison.Ordinal);

        Advance(scheduler, 132);
        Assert.Contains("- read", console.Writes[^1], StringComparison.Ordinal);

        Advance(scheduler, 99);
        Assert.Contains("\\ read", console.Writes[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_resize_pushed_through_resized_reflows_the_next_frame()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(24, 10));
        using var area = new LiveArea(console, scheduler);
        area.Start();

        area.PostKey(new KeyEvent(KeyKind.Paste, "abcdefg", false, false, false));
        Advance(scheduler, 33);

        console.PushResize(new ConsoleSize(8, 8));
        Advance(scheduler, 33);

        Assert.Contains("  g", console.Writes[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Render_callbacks_are_single_writer_and_never_reentrant()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(40, 10));
        var tracking = new TrackingConsoleIO(console);
        using var area = new LiveArea(tracking, scheduler);
        area.Start();

        for (var i = 0; i < 5; i++)
        {
            Type(area, i.ToString());
            area.AppendTail("x\n");
            Advance(scheduler, 33);
        }

        Assert.Equal(1, tracking.MaxDepth);
        Assert.True(console.Writes.Count > 1);
    }

    [Fact]
    public void Dispose_clears_the_area_and_stops_painting()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(40, 10));
        var area = new LiveArea(console, scheduler);
        area.Start();
        area.PostKey(Char("a"));
        Advance(scheduler, 33);

        int before = console.Writes.Count;
        area.Dispose();
        Advance(scheduler, 1);

        Assert.Equal(before + 1, console.Writes.Count);
        Assert.Contains("\u001b[1A", console.Writes[^1], StringComparison.Ordinal);

        int afterClear = console.Writes.Count;
        Advance(scheduler, 1000);

        Assert.Equal(afterClear, console.Writes.Count);

        area.Dispose();
    }

    [Fact]
    public void Dispose_with_a_pending_render_schedules_exactly_one_clear()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(40, 10));
        var area = new LiveArea(console, scheduler);
        area.Start();
        area.PostKey(Char("a"));
        Advance(scheduler, 33);

        int before = console.Writes.Count;
        area.PostKey(Char("b"));
        area.Dispose();

        Assert.Equal(before, console.Writes.Count);

        Advance(scheduler, 1000);

        Assert.Equal(before + 1, console.Writes.Count);
        Assert.Contains("\u001b[1A", console.Writes[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Footer_values_come_through_to_the_rendered_frame()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(60, 10));
        using var area = new LiveArea(console, scheduler);
        area.Start();

        area.SetFooter("test-model", 1200, 340, 12.5, "/repo", "main");
        Advance(scheduler, 33);

        Assert.Contains(
            "test-model · 1540 tok · 12.5% ctx · /repo · main",
            console.Writes[^1],
            StringComparison.Ordinal
        );
    }

    private static string Text(FakeConsoleIO console) => string.Concat(console.Writes);

    private static KeyEvent Char(string text) => new(KeyKind.Character, text, false, false, false);

    private static void Type(LiveArea area, string text)
    {
        foreach (char c in text)
        {
            area.PostKey(Char(c.ToString()));
        }
    }

    private static void Advance(TestScheduler scheduler, int milliseconds) =>
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(milliseconds).Ticks);

    private sealed class TrackingConsoleIO : IConsoleIO
    {
        private readonly FakeConsoleIO _inner;
        private int _depth;

        public TrackingConsoleIO(FakeConsoleIO inner) => _inner = inner;

        public int MaxDepth { get; private set; }

        public bool IsInteractive => _inner.IsInteractive;

        public ConsoleSize Size => _inner.Size;

        public IObservable<ConsoleSize> Resized => _inner.Resized;

        public IAsyncEnumerable<KeyEvent> ReadKeysAsync(CancellationToken cancellationToken) =>
            _inner.ReadKeysAsync(cancellationToken);

        public void Write(string text)
        {
            _depth++;
            MaxDepth = Math.Max(MaxDepth, _depth);
            _inner.Write(text);
            _depth--;
        }

        public IDisposable EnterRawMode() => _inner.EnterRawMode();
    }
}
