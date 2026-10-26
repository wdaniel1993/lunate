using System.Text.RegularExpressions;
using Microsoft.Reactive.Testing;

namespace Lunate.Tui.Tests;

/// <summary>
/// The notice slot's one guarantee: a notice that was posted is painted before a clear can take it.
/// A clear that arrives between the post and the frame tick (the turn starts, the next submission
/// lands) is held for one frame, so the notice the user was meant to see shows once; the frame
/// after that drops it. Without the hold, a clear in that window discards the notice for good and
/// no frame ever contains it.
/// </summary>
public sealed class NoticeSlotTests
{
    private const string Notice = "hook InputReceived was slow";

    [Fact]
    public void A_clear_before_the_paint_shows_the_notice_once()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(60, 12));
        using var area = new LiveArea(console, scheduler);
        var log = new NoticeSlotLog();
        area.ObserveNotices(log.Record);
        area.Start();

        area.SetNotice(Notice);
        // The clear arrives before any frame has shown the notice.
        area.SetNotice(null);
        Advance(scheduler, 33);

        Assert.Contains(Notice, Visible(console.Writes[^1]), StringComparison.Ordinal);
        Assert.Empty(log.LostNotices);

        // Shown once and then gone: the held clear takes effect on the frame after the paint.
        Advance(scheduler, 33);

        Assert.DoesNotContain(Notice, Visible(console.Writes[^1]), StringComparison.Ordinal);
        Assert.Single(log.Changes(NoticeSlotChange.Painted));
    }

    [Fact]
    public void A_clear_after_the_paint_is_not_deferred()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(60, 12));
        using var area = new LiveArea(console, scheduler);
        var log = new NoticeSlotLog();
        area.ObserveNotices(log.Record);
        area.Start();

        area.SetNotice(Notice);
        Advance(scheduler, 33);
        Assert.Contains(Notice, Visible(console.Writes[^1]), StringComparison.Ordinal);

        area.SetNotice(null);
        Advance(scheduler, 33);

        Assert.DoesNotContain(Notice, Visible(console.Writes[^1]), StringComparison.Ordinal);
        NoticeSlotEntry clear = Assert.Single(log.Changes(NoticeSlotChange.Cleared));
        Assert.False(clear.Deferred, log.ToString());
        Assert.Empty(log.LostNotices);
    }

    /// <summary>
    /// A notice posted while a clear is held belongs to a newer generation: it paints, and the held
    /// clear never reaches it.
    /// </summary>
    [Fact]
    public void A_new_notice_supersedes_a_held_clear()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(60, 12));
        using var area = new LiveArea(console, scheduler);
        var log = new NoticeSlotLog();
        area.ObserveNotices(log.Record);
        area.Start();

        area.SetNotice("first notice");
        area.SetNotice(null);
        area.SetNotice("second notice");
        Advance(scheduler, 33);
        Advance(scheduler, 33);

        Assert.Contains("second notice", Visible(console.Writes[^1]), StringComparison.Ordinal);
        Assert.Empty(log.LostNotices);
    }

    private static string Visible(string text) =>
        Regex.Replace(text, "\u001b\\[[0-9;]*m", string.Empty);

    private static void Advance(TestScheduler scheduler, int milliseconds) =>
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(milliseconds).Ticks);
}
