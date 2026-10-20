using Lunate.Agent;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionSessionSelectionTests
{
    [Fact]
    public async Task Continue_loads_the_newest_session_recorded_for_the_working_directory()
    {
        using var host = new InteractiveSessionHost(launch: new LaunchOptions { Continue = true });
        string cwd = new Workspace(host.Temp.Root).WorktreeRoot;
        string sessions = host.Temp.File("sessions");
        SeedSession(
            sessions,
            cwd,
            "older question",
            new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc)
        );
        SeedSession(
            sessions,
            cwd,
            "newest question",
            new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc)
        );
        SeedSession(
            sessions,
            host.Temp.File("elsewhere"),
            "other directory",
            new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc)
        );
        host.Client.Enqueue(Scripts.Text("answer"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("next question");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        string[] texts = RequestTexts(host, 0);
        Assert.Contains("newest question", texts);
        Assert.DoesNotContain("older question", texts);
        Assert.DoesNotContain("other directory", texts);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Session_flag_loads_a_session_by_id()
    {
        using var store = new TempDirectory();
        string sessions = store.File("sessions");
        Session seeded = SeedSession(
            sessions,
            new Workspace(store.Root).WorktreeRoot,
            "remembered question",
            DateTime.UtcNow
        );
        using var host = new InteractiveSessionHost(
            workingDirectory: store.Root,
            sessionsRoot: sessions,
            launch: new LaunchOptions { Session = seeded.SessionId }
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
    public async Task Session_flag_loads_a_session_by_path()
    {
        using var store = new TempDirectory();
        Session seeded = SeedSession(
            store.File("sessions"),
            new Workspace(store.Root).WorktreeRoot,
            "path question",
            DateTime.UtcNow
        );
        using var host = new InteractiveSessionHost(
            workingDirectory: store.Root,
            launch: new LaunchOptions { Session = seeded.Path }
        );
        host.Client.Enqueue(Scripts.Text("answer"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("next question");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        Assert.Contains("path question", RequestTexts(host, 0));

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task No_session_writes_no_file_after_a_prompt()
    {
        using var host = new InteractiveSessionHost(launch: new LaunchOptions { NoSession = true });
        host.Client.Enqueue(Scripts.Text("answer"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "answer");

        string sessions = host.Temp.File("sessions");
        Assert.Empty(Directory.Exists(sessions) ? Directory.GetFiles(sessions, "*.jsonl") : []);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task No_session_new_stays_fileless()
    {
        using var host = new InteractiveSessionHost(launch: new LaunchOptions { NoSession = true });
        host.Client.Enqueue(Scripts.Text("first answer"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("second answer"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("one");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "first answer");

        host.Console.SendText("/new");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "new session");

        host.Console.SendText("two");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);
        Assert.Equal(["two"], RequestTexts(host, 1));

        string sessions = host.Temp.File("sessions");
        Assert.Empty(Directory.Exists(sessions) ? Directory.GetFiles(sessions, "*.jsonl") : []);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Resume_opens_the_picker_after_composition()
    {
        using var host = new InteractiveSessionHost(launch: new LaunchOptions { Resume = true });
        host.Client.Enqueue(Scripts.Text("answer"), Scripts.Stop());
        Task run = host.RunAsync();

        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select session");
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
    public async Task Missing_session_id_is_a_notice_and_the_session_still_runs()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Session = "missing-id" }
        );
        host.Client.Enqueue(Scripts.Text("answer"), Scripts.Stop());
        Task run = host.RunAsync();

        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "no session with id 'missing-id'"
        );

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        Assert.Contains("hi", RequestTexts(host, 0));

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Missing_session_path_is_a_notice_naming_it()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            "lunate-missing-" + Guid.NewGuid().ToString("N") + ".jsonl"
        );
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { Session = missing }
        );
        host.Console.Size = new ConsoleSize(300, 24);
        host.Client.Enqueue(Scripts.Text("answer"), Scripts.Stop());
        Task run = host.RunAsync();

        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            $"could not load session '{missing}'"
        );

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        Assert.Contains("hi", RequestTexts(host, 0));

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Continue_without_a_matching_session_names_the_working_directory()
    {
        using var host = new InteractiveSessionHost(launch: new LaunchOptions { Continue = true });
        host.Console.Size = new ConsoleSize(300, 24);
        string cwd = new Workspace(host.Temp.Root).WorktreeRoot;
        host.Client.Enqueue(Scripts.Text("answer"), Scripts.Stop());
        Task run = host.RunAsync();

        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            $"no session for '{cwd}'; run a prompt to start one"
        );

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        Assert.Contains("hi", RequestTexts(host, 0));

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Ambiguous_session_id_names_the_matching_paths()
    {
        using var store = new TempDirectory();
        string sessions = store.File("sessions");
        Session seeded = SeedSession(
            sessions,
            new Workspace(store.Root).WorktreeRoot,
            "remembered question",
            DateTime.UtcNow
        );
        string duplicate = Path.Combine(
            sessions,
            "duplicate-" + Guid.NewGuid().ToString("N") + ".jsonl"
        );
        File.Copy(seeded.Path, duplicate);
        using var host = new InteractiveSessionHost(
            workingDirectory: store.Root,
            sessionsRoot: sessions,
            launch: new LaunchOptions { Session = seeded.SessionId }
        );
        host.Console.Size = new ConsoleSize(500, 24);
        host.Client.Enqueue(Scripts.Text("answer"), Scripts.Stop());
        Task run = host.RunAsync();

        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "is ambiguous:");
        Assert.Contains(seeded.Path, host.Frames, StringComparison.Ordinal);
        Assert.Contains(duplicate, host.Frames, StringComparison.Ordinal);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        Assert.Contains("hi", RequestTexts(host, 0));
        Assert.DoesNotContain("remembered question", RequestTexts(host, 0));

        host.Console.Complete();
        await run;
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

    private static Session SeedSession(string root, string cwd, string text, DateTime modified)
    {
        Directory.CreateDirectory(root);
        string provisional = Path.Combine(root, Path.GetRandomFileName());
        Session created = Session.Create(provisional, cwd, null, cwd);
        created.AppendMessage(new ChatMessage(ChatRole.User, text));
        string path = Path.Combine(root, SessionPaths.SessionFileName(created.SessionId));
        File.Move(provisional, path);
        File.SetLastWriteTimeUtc(path, modified);
        return Session.Load(path);
    }
}
