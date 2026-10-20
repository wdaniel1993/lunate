using Lunate.Agent;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The interactive frontend's side of design D5: a fresh session records the launch directives and
/// composes with them, a picker resume replays the recorded sections, and <c>/new</c> rebuilds the
/// harness from the new session's own directives.
/// </summary>
public sealed class InteractiveSessionSystemPromptTests
{
    [Fact]
    public async Task A_fresh_session_records_and_composes_the_launch_replacement()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { SystemPrompt = "the interactive contract" }
        );
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        Assert.Equal("the interactive contract", host.Client.Requests[0][0].Text);
        SessionPromptSectionEntry section = Assert.Single(
            LoadOnlySession(host).Entries.OfType<SessionPromptSectionEntry>()
        );
        Assert.Equal("system", section.Section);
        Assert.Equal("the interactive contract", section.Text);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Resume_from_the_picker_replays_the_recorded_replacement()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        Session seeded = SeedSession(
            host.Temp.File("sessions"),
            new Workspace(host.Temp.Root).WorktreeRoot,
            "remembered question"
        );
        seeded.AppendPromptSection("system", "the recorded contract");

        host.Console.SendText("/resume");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select session");
        // The picker opens on the fresh startup session; the seeded one is the newest row above it.
        host.Console.Send(new KeyEvent(KeyKind.Up, null, false, false, false));
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, $"resumed {seeded.SessionId}");

        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        host.Console.SendText("next question");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        Assert.Equal("the recorded contract", host.Client.Requests[0][0].Text);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_launch_replacement_still_applies_after_new()
    {
        using var host = new InteractiveSessionHost(
            launch: new LaunchOptions { SystemPrompt = "the interactive contract" }
        );
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("second"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("one");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        Assert.Equal("the interactive contract", host.Client.Requests[0][0].Text);

        host.Console.SendText("/new");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "new session");

        host.Console.SendText("two");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);
        Assert.Equal("the interactive contract", host.Client.Requests[1][0].Text);

        string[] files = Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl");
        Assert.Equal(2, files.Length);
        Assert.All(
            files,
            file =>
                Assert.Contains(
                    Session.Load(file).Entries.OfType<SessionPromptSectionEntry>(),
                    entry => entry.Section == "system" && entry.Text == "the interactive contract"
                )
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task New_after_a_resumed_session_uses_the_composed_prompt()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        Session seeded = SeedSession(
            host.Temp.File("sessions"),
            new Workspace(host.Temp.Root).WorktreeRoot,
            "remembered question"
        );
        seeded.AppendPromptSection("system", "the recorded contract");

        host.Console.SendText("/resume");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select session");
        // The picker opens on the fresh startup session; the seeded one is the newest row above it.
        host.Console.Send(new KeyEvent(KeyKind.Up, null, false, false, false));
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, $"resumed {seeded.SessionId}");
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Console.SendText("one");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        Assert.Equal("the recorded contract", host.Client.Requests[0][0].Text);

        host.Console.SendText("/new");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "new session");
        host.Client.Enqueue(Scripts.Text("second"), Scripts.Stop());
        host.Console.SendText("two");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);
        Assert.StartsWith(
            "You are Lunate",
            host.Client.Requests[1][0].Text,
            StringComparison.Ordinal
        );

        host.Console.Complete();
        await run;
    }

    private static Session LoadOnlySession(InteractiveSessionHost host) =>
        Session.Load(Assert.Single(Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl")));

    private static Session SeedSession(string directory, string cwd, string text)
    {
        Directory.CreateDirectory(directory);
        string provisional = Path.Combine(directory, Path.GetRandomFileName());
        Session created = Session.Create(provisional, cwd, null, cwd);
        created.AppendMessage(new ChatMessage(ChatRole.User, text));
        string path = Path.Combine(directory, SessionPaths.SessionFileName(created.SessionId));
        File.Move(provisional, path);
        return Session.Load(path);
    }
}
