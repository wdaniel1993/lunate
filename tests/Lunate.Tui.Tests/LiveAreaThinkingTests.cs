using Microsoft.Reactive.Testing;

namespace Lunate.Tui.Tests;

/// <summary>
/// The live thinking block on the pipeline: the elapsed seconds come from the spinner tick on the
/// injected scheduler, counted from the first reasoning text.
/// </summary>
public sealed class LiveAreaThinkingTests
{
    [Fact]
    public void A_spinner_tick_advances_the_elapsed_seconds()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(40, 10));
        using var area = new LiveArea(console, scheduler);
        area.Start();

        area.SetThinking("weighing the options");
        Advance(scheduler, 33);
        Assert.Contains("⠹ thinking… 0s", console.Writes[^1], StringComparison.Ordinal);

        Advance(scheduler, 1100);
        Assert.Contains("⠹ thinking… 1s", console.Writes[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void The_elapsed_seconds_count_from_the_first_thinking_text()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(40, 10));
        using var area = new LiveArea(console, scheduler);
        area.Start();

        area.SetThinking("first fragment");
        Advance(scheduler, 500);
        area.SetThinking("first fragment, continued");
        Advance(scheduler, 800);

        Assert.Contains("⠹ thinking… 1s", console.Writes[^1], StringComparison.Ordinal);
    }

    private static void Advance(TestScheduler scheduler, int milliseconds) =>
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(milliseconds).Ticks);
}
