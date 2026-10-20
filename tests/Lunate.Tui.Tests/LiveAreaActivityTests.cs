using Microsoft.Reactive.Testing;

namespace Lunate.Tui.Tests;

/// <summary>
/// The activity clocks on the pipeline: the run's and the active tool's elapsed seconds advance
/// only from spinner ticks on the injected scheduler, and the tool clock restarts only when the
/// tool actually changes.
/// </summary>
public sealed class LiveAreaActivityTests
{
    [Fact]
    public void The_run_elapsed_advances_from_spinner_ticks()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(80, 10));
        using var area = new LiveArea(console, scheduler);
        area.Start();

        area.SetRunning(true);
        Advance(scheduler, 33);
        Assert.Contains("working… 0s", console.Writes[^1], StringComparison.Ordinal);

        Advance(scheduler, 1100);
        Assert.Contains("working… 1s", console.Writes[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void The_tool_elapsed_advances_from_spinner_ticks()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(80, 10));
        using var area = new LiveArea(console, scheduler);
        area.Start();

        area.SetTool("read");
        Advance(scheduler, 33);
        Assert.Contains("read · 0s", console.Writes[^1], StringComparison.Ordinal);

        Advance(scheduler, 1100);
        Assert.Contains("read · 1s", console.Writes[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Setting_the_same_tool_name_keeps_the_clock()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(80, 10));
        using var area = new LiveArea(console, scheduler);
        area.Start();

        area.SetTool("read");
        Advance(scheduler, 1100);
        area.SetTool("read");
        Advance(scheduler, 33);

        Assert.Contains("read · 1s", console.Writes[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_tool_name_restarts_the_clock()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(80, 10));
        using var area = new LiveArea(console, scheduler);
        area.Start();

        area.SetTool("read");
        Advance(scheduler, 1100);
        area.SetTool("bash");
        Advance(scheduler, 33);

        Assert.Contains("bash · 0s", console.Writes[^1], StringComparison.Ordinal);
    }

    private static void Advance(TestScheduler scheduler, int milliseconds) =>
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(milliseconds).Ticks);
}
