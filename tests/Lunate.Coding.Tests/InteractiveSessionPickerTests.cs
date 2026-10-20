using Lunate.Agent;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionPickerTests
{
    public static TheoryData<string> StartupSelections => new() { "--continue", "--session" };

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
    public async Task Startup_resume_opens_the_picker_after_composition()
    {
        using var host = new InteractiveSessionHost(launch: new LaunchOptions { Resume = true });
        Session seeded = SeedSession(
            host.Temp.File("sessions"),
            new Workspace(host.Temp.Root).WorktreeRoot,
            "remembered question"
        );
        host.Client.Enqueue(Scripts.Text("answer"), Scripts.Stop());
        Task run = host.RunAsync();

        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select session");
        Assert.Contains(seeded.SessionId, host.LastFrame, StringComparison.Ordinal);

        host.Console.SendEscape();
        await InteractiveSessionCommandTests.WaitForFrameGoneAsync(host, "Select session");

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        Assert.Contains("hi", RequestTexts(host, 0));

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Startup_resume_does_not_create_a_session_until_a_turn_starts()
    {
        using var host = new InteractiveSessionHost(launch: new LaunchOptions { Resume = true });
        Task run = host.RunAsync();

        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select session");
        Assert.Empty(SessionFiles(host));

        host.Console.SendEscape();
        await InteractiveSessionCommandTests.WaitForFrameGoneAsync(host, "Select session");
        Assert.Empty(SessionFiles(host));

        host.Client.Enqueue(Scripts.Text("answer"), Scripts.Stop());
        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "answer");

        Assert.Single(SessionFiles(host));

        host.Console.Complete();
        await run;
    }

    [Theory]
    [MemberData(nameof(StartupSelections))]
    public async Task Startup_selection_seeds_the_first_request(string flag)
    {
        using var store = new TempDirectory();
        string sessions = store.File("sessions");
        Session seeded = SeedSession(
            sessions,
            new Workspace(store.Root).WorktreeRoot,
            "remembered question"
        );
        using var host = new InteractiveSessionHost(
            workingDirectory: store.Root,
            sessionsRoot: sessions,
            launch: flag == "--continue"
                ? new LaunchOptions { Continue = true }
                : new LaunchOptions { Session = seeded.SessionId }
        );
        host.Client.Enqueue(Scripts.Text("answer"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("next question");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        Assert.Contains("remembered question", RequestTexts(host, 0));

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Start_session_seam_loads_the_given_file_instead_of_creating_one()
    {
        using var store = new TempDirectory();
        Session seeded = SeedSession(
            store.File("sessions"),
            new Workspace(store.Root).WorktreeRoot,
            "remembered question"
        );
        using var host = new InteractiveSessionHost(
            workingDirectory: store.Root,
            startSession: seeded.Path
        );
        host.Client.Enqueue(Scripts.Text("answer"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("next question");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        Assert.Contains("remembered question", RequestTexts(host, 0));
        Assert.Equal(
            seeded.Path,
            Assert.Single(Directory.GetFiles(store.File("sessions"), "*.jsonl"))
        );
        Assert.Empty(
            Directory.Exists(host.Temp.File("sessions"))
                ? Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl")
                : []
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Start_with_picker_seam_opens_the_picker_after_composition()
    {
        using var host = new InteractiveSessionHost(startWithPicker: true);
        Task run = host.RunAsync();

        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select session");
        host.Console.SendEscape();
        await InteractiveSessionCommandTests.WaitForFrameGoneAsync(host, "Select session");
        Assert.Empty(host.Client.Requests);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_picker_labels_a_titled_session_with_its_title_and_id()
    {
        using var host = new InteractiveSessionHost(startWithPicker: true);
        Session seeded = SeedSession(
            host.Temp.File("sessions"),
            new Workspace(host.Temp.Root).WorktreeRoot,
            "remembered question"
        );
        SessionTitle.Write(seeded.Path, "Refactor the parser");
        Task run = host.RunAsync();

        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "Refactor the parser — " + seeded.SessionId
        );

        host.Console.SendEscape();
        await InteractiveSessionCommandTests.WaitForFrameGoneAsync(host, "Select session");
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

    private static string[] SessionFiles(InteractiveSessionHost host)
    {
        string directory = host.Temp.File("sessions");
        return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.jsonl") : [];
    }

    private static string[] RequestTexts(InteractiveSessionHost host, int request) =>
        [
            .. host
                .Client.Requests[request]
                .Where(message =>
                    message.Role != ChatRole.System && !string.IsNullOrEmpty(message.Text)
                )
                .Select(message => message.Text),
        ];

    private static Session SeedSession(string root, string cwd, string text)
    {
        Directory.CreateDirectory(root);
        string provisional = Path.Combine(root, Path.GetRandomFileName());
        Session created = Session.Create(provisional, cwd, null, cwd);
        created.AppendMessage(new ChatMessage(ChatRole.User, text));
        string path = Path.Combine(root, SessionPaths.SessionFileName(created.SessionId));
        File.Move(provisional, path);
        return Session.Load(path);
    }
}
