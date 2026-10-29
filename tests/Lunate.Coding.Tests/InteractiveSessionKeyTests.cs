using Lunate.Tui;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionKeyTests
{
    [Fact]
    public async Task Up_and_down_navigate_the_history_on_the_input_loop()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("hello");
        host.Console.SendEnter();

        // The committed scrollback is the race-free completion signal: when the run ends, the
        // live tail is cleared right after the commit, so a frame-based wait would race the
        // commit and lose deterministically on the slower Windows runners.
        await host.WaitForScrollbackAsync("ok");

        host.Console.Send(new KeyEvent(KeyKind.Up, null, false, false, false));
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("> hello", StringComparison.Ordinal);
        });

        host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !host.LastFrame.Contains("> hello", StringComparison.Ordinal);
        });

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task CtrlC_clears_non_empty_input_and_a_second_empty_press_quits()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.SendText("abc");
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("> abc", StringComparison.Ordinal);
        });

        host.Console.SendCtrlC();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !host.LastFrame.Contains("abc", StringComparison.Ordinal);
        });

        host.Console.SendCtrlC();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("press Ctrl+C again to quit", StringComparison.Ordinal);
        });

        host.Console.SendCtrlC();
        await run;
    }

    [Fact]
    public async Task The_quit_window_expires_on_virtual_time()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.SendCtrlC();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("press Ctrl+C again to quit", StringComparison.Ordinal);
        });

        host.Advance(2100);
        host.Console.SendCtrlC();
        host.Console.SendCtrlC();
        await run;

        Assert.False(host.Session.IsRunning);
    }

    [Fact]
    public async Task Escape_while_idle_does_nothing()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.SendText("draft");
        host.Console.SendEscape();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("> draft", StringComparison.Ordinal);
        });

        Assert.Empty(host.Client.Requests);
        Assert.False(host.Session.IsRunning);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task CtrlC_interrupts_a_running_turn_without_touching_the_draft()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        host.Console.SendCtrlC();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        Assert.Single(host.Client.Requests);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task CtrlD_quits_on_a_second_press_from_an_empty_prompt()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendCtrlD();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("press Ctrl+D again to quit", StringComparison.Ordinal);
        });

        host.Console.SendCtrlD();
        await run;
    }

    [Fact]
    public async Task CtrlD_does_not_quit_while_the_prompt_holds_text()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("draft");
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("> draft", StringComparison.Ordinal);
        });

        host.Console.SendCtrlD();
        host.Console.SendCtrlD();
        host.Advance(33);

        Assert.False(run.IsCompleted);
        Assert.Contains("> draft", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task CtrlL_redraws_the_prompt_without_losing_the_input()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("draft");
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("> draft", StringComparison.Ordinal);
        });

        host.Console.SendCtrlL();
        host.Advance(33);

        Assert.Contains("> draft", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("Select model", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Kill_and_yank_round_trip_on_the_input_loop()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hello world");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "hello world");

        host.Console.SendCtrl("w");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "hello ");

        host.Console.SendCtrl("y");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "hello world");

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Undo_restores_the_buffer_after_a_kill()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("alpha");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "alpha");

        host.Console.SendCtrl("w");
        await host.WaitUntilAsync(() => host.Session.Input.Text.Length == 0);

        host.Console.SendCtrl("_");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "alpha");

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Ctrl_A_moves_to_the_line_start_for_the_next_insert()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("bc");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "bc");

        host.Console.SendCtrl("a");
        host.Console.SendText("a");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "abc");

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task History_search_walks_matches_and_Esc_restores_the_draft()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("one"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("two"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("first entry");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => !host.Session.IsRunning && host.Session.IsComposed);

        host.Console.SendText("second entry");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => !host.Session.IsRunning && host.Session.IsComposed);

        host.Console.SendText("draft");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "draft");

        host.Console.SendCtrl("r");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "second entry");

        host.Console.SendCtrl("r");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "first entry");

        host.Console.SendEscape();
        await host.WaitUntilAsync(() => host.Session.Input.Text == "draft");

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task History_search_narrows_on_typed_text()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("one"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("two"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("alpha");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => !host.Session.IsRunning && host.Session.IsComposed);

        host.Console.SendText("beta");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => !host.Session.IsRunning && host.Session.IsComposed);

        host.Console.SendCtrl("r");
        host.Console.SendText("al");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "alpha");

        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.Input.Text == "alpha");

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Ctrl_S_stashes_and_restores_the_draft()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("half a thought");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "half a thought");

        host.Console.SendCtrl("s");
        await host.WaitUntilAsync(() => host.Session.Input.Text.Length == 0);

        host.Console.SendCtrl("s");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "half a thought");

        host.Console.Complete();
        await run;
    }

    /// <summary>
    /// The stash sequence the keys-trailer VHS recording was read as showing: two drafts, Ctrl+S
    /// after each, then Ctrl+R — replayed deterministically at the unit level over the scripted
    /// console. The catalog's single stash slot holds one draft and restore happens only on an
    /// empty prompt, so the second Ctrl+S on a draft in the prompt stashes the newer draft; the
    /// prompt carries exactly one draft at every step, never both concatenated, and each stash
    /// posts its stash notice once. A red here would mean the tape was reporting a real defect.
    /// </summary>
    [Fact]
    public async Task Two_stashes_in_a_row_never_concatenate_the_drafts()
    {
        using var host = new InteractiveSessionHost();
        var log = new NoticeSlotLog();
        host.Session.ObserveNotices(log.Record);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("first draft");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "first draft");

        host.Console.SendCtrl("s");
        await host.WaitUntilAsync(() => host.Session.Input.Text.Length == 0);
        await host.WaitUntilAsync(() => log.Changes(NoticeSlotChange.Posted).Count == 1);
        NoticeSlotEntry firstStash = Assert.Single(log.Changes(NoticeSlotChange.Posted));
        Assert.Contains("draft stashed", firstStash.Text, StringComparison.Ordinal);

        host.Console.SendText("second draft");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "second draft");

        // The prompt is non-empty, so the second Ctrl+S is a second stash: the newer draft
        // replaces the older one in the slot, and the prompt shows exactly one draft.
        host.Console.SendCtrl("s");
        await host.WaitUntilAsync(() => host.Session.Input.Text.Length == 0);
        // The prompt clears before the notice is posted, so read the log by count, not by timing:
        // the first stash above waits for its count for exactly this reason.
        await host.WaitUntilAsync(() => log.Changes(NoticeSlotChange.Posted).Count == 2);
        IReadOnlyList<NoticeSlotEntry> afterSecondStash = log.Changes(NoticeSlotChange.Posted);
        Assert.Equal(2, afterSecondStash.Count);
        Assert.Contains(
            "draft stashed",
            afterSecondStash[afterSecondStash.Count - 1].Text,
            StringComparison.Ordinal
        );

        // Restore happens on the empty prompt: exactly one draft comes back, and it is the
        // newer one — never the two drafts joined together.
        host.Console.SendCtrl("s");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "second draft");
        await host.WaitUntilAsync(() => log.Changes(NoticeSlotChange.Posted).Count == 3);
        IReadOnlyList<NoticeSlotEntry> afterRestore = log.Changes(NoticeSlotChange.Posted);
        Assert.Equal(3, afterRestore.Count);
        Assert.Contains(
            "draft restored",
            afterRestore[afterRestore.Count - 1].Text,
            StringComparison.Ordinal
        );

        host.Console.SendCtrl("r");
        await host.WaitUntilAsync(() => log.Changes(NoticeSlotChange.Posted).Count == 4);
        // The search opens over the prompt as it stands: still exactly the one restored draft.
        Assert.Equal("second draft", host.Session.Input.Text);
        NoticeSlotEntry historySearch = log.Changes(NoticeSlotChange.Posted)[
            log.Changes(NoticeSlotChange.Posted).Count - 1
        ];
        Assert.Contains("history:", historySearch.Text, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }
}
