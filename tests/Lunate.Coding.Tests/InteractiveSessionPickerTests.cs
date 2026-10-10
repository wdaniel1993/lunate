using Lunate.Agent;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionPickerTests
{
    [Fact]
    public async Task Ctrl_L_opens_the_model_picker_and_Esc_dismisses_without_changing()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.Send(
            new KeyEvent(KeyKind.Character, "l", Ctrl: true, Shift: false, Alt: false)
        );
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select model");
        string frame = host.LastFrame;
        Assert.Contains("* gpt-4o-mini (openai)", frame, StringComparison.Ordinal);
        Assert.Contains("> * gpt-4o-mini (openai)", frame, StringComparison.Ordinal);
        Assert.Contains("  gpt-4o (openai)", frame, StringComparison.Ordinal);
        Assert.Contains("  claude-sonnet-5-5 (anthropic)", frame, StringComparison.Ordinal);
        Assert.Contains("  claude-opus-5-5 (anthropic)", frame, StringComparison.Ordinal);

        host.Console.SendEscape();
        await InteractiveSessionCommandTests.WaitForFrameGoneAsync(host, "Select model");

        Assert.Empty(host.Client.Requests);
        Session session = LoadOnlySession(host);
        Assert.Empty(session.Entries.OfType<SessionModelChangeEntry>());
        Assert.Contains("gpt-4o-mini", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Choosing_a_model_switches_and_keeps_the_conversation()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("ok2"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "ok");

        host.Console.Send(
            new KeyEvent(KeyKind.Character, "l", Ctrl: true, Shift: false, Alt: false)
        );
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select model");
        host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "> gpt-4o (openai)");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "model: gpt-4o");

        Assert.Contains("gpt-4o ·", host.LastFrame, StringComparison.Ordinal);
        SessionModelChangeEntry change = Assert.Single(
            LoadOnlySession(host).Entries.OfType<SessionModelChangeEntry>()
        );
        Assert.Equal("gpt-4o", change.Model);

        host.Console.SendText("after switch");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);

        string[] texts =
        [
            .. host
                .Client.Requests[1]
                .Where(message =>
                    message.Role != Microsoft.Extensions.AI.ChatRole.System
                    && !string.IsNullOrEmpty(message.Text)
                )
                .Select(message => message.Text),
        ];
        Assert.Equal(["hi", "ok", "after switch"], texts);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Picker_navigation_clamps_and_ignores_every_other_key()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.Send(
            new KeyEvent(KeyKind.Character, "l", Ctrl: true, Shift: false, Alt: false)
        );
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select model");

        for (var index = 0; index < 10; index++)
        {
            host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        }

        // The grace period keeps the overdue ticks flowing while the last key is processed.
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains(
                "> claude-opus-5-5 (anthropic)",
                StringComparison.Ordinal
            );
        });

        for (var index = 0; index < 10; index++)
        {
            host.Console.Send(new KeyEvent(KeyKind.Up, null, false, false, false));
        }

        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("> * gpt-4o-mini (openai)", StringComparison.Ordinal);
        });

        // Characters do not reach the input line, and Enter confirms the selection.
        host.Console.SendText("abc");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "model: gpt-4o-mini");
        Assert.DoesNotContain("> abc", host.LastFrame, StringComparison.Ordinal);
        Assert.Empty(host.Client.Requests);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Model_with_an_id_switches_directly()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.SendText("/model gpt-4o");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "model: gpt-4o");

        Assert.DoesNotContain("Select model", host.LastFrame, StringComparison.Ordinal);
        Assert.Empty(host.Client.Requests);
        SessionModelChangeEntry change = Assert.Single(
            LoadOnlySession(host).Entries.OfType<SessionModelChangeEntry>()
        );
        Assert.Equal("gpt-4o", change.Model);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task An_unknown_model_id_is_a_notice_without_a_change()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.SendText("/model nope");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "unknown model: nope");

        Assert.Empty(LoadOnlySession(host).Entries.OfType<SessionModelChangeEntry>());
        Assert.DoesNotContain("Select model", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task New_starts_a_fresh_session()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("a1"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("a2"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("one");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "a1");

        host.Console.SendText("/new");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "new session");

        host.Console.SendText("again");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);

        string[] texts =
        [
            .. host
                .Client.Requests[1]
                .Where(message => message.Role != Microsoft.Extensions.AI.ChatRole.System)
                .Select(message => message.Text),
        ];
        Assert.Equal(["again"], texts);
        Assert.Equal(2, Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl").Length);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Resume_restores_the_chosen_session()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("first answer"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("second answer"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("first question");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "first answer");

        host.Console.SendText("/new");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "new session");

        string oldPath = Array.Find(
            Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl"),
            path =>
                Session
                    .Load(path)
                    .Entries.OfType<SessionMessageEntry>()
                    .Any(entry => entry.Message.Text == "first question")
        )!;
        string oldId = Path.GetFileNameWithoutExtension(oldPath);

        host.Console.SendText("/resume");
        host.Console.SendEnter();
        // The current session is listed first (newest); Down selects the older one.
        host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "resumed " + oldId);

        host.Console.SendText("second question");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);

        string[] texts =
        [
            .. host
                .Client.Requests[1]
                .Where(message =>
                    message.Role != Microsoft.Extensions.AI.ChatRole.System
                    && !string.IsNullOrEmpty(message.Text)
                )
                .Select(message => message.Text),
        ];
        Assert.Equal(["first question", "first answer", "second question"], texts);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Opening_the_picker_while_busy_is_refused()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("never"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        host.Console.Send(
            new KeyEvent(KeyKind.Character, "l", Ctrl: true, Shift: false, Alt: false)
        );
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "a turn is running");
        Assert.DoesNotContain("Select model", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_approval_prompt_blocks_the_picker()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(
            Scripts.Call("call-1", "write", Scripts.Args(("path", "note.txt"), ("content", "x"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);

        host.Console.Send(
            new KeyEvent(KeyKind.Character, "l", Ctrl: true, Shift: false, Alt: false)
        );
        host.Console.SendText("n");
        await host.Client.WaitForCallAsync(2);

        Assert.DoesNotContain("Select model", host.Frames, StringComparison.Ordinal);
        host.Console.Complete();
        await run;
    }

    private static Session LoadOnlySession(InteractiveSessionHost host) =>
        Session.Load(Assert.Single(Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl")));
}
