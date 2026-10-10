using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>Golden files for the scripted end-to-end snapshot, regenerated deliberately.</summary>
internal static class InteractiveGoldens
{
    public static bool UpdateRequested =>
        Environment.GetEnvironmentVariable("LUNATE_CODING_UPDATE_GOLDENS") == "1";

    public static string Directory =>
        Path.Combine(RepositoryRoot(), "tests", "Lunate.Coding.Tests", "fixtures", "interactive");

    private static string RepositoryRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "lunate.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find lunate.sln.");
    }

    public static void AssertMatchesText(string name, string actual)
    {
        string path = Path.Combine(Directory, name);
        if (UpdateRequested)
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(path, actual);
        }

        Assert.Equal(File.ReadAllText(path), actual);
    }
}

public sealed class InteractiveSessionEndToEndTests
{
    private const string EscapeNext = "\u001b";

    [Fact]
    public async Task A_scripted_session_matches_the_committed_scrollback_and_final_frame()
    {
        Func<string, string?> baseEnvironment = PrintModeTestSupport.Environment();
        using var host = new InteractiveSessionHost(environment: name =>
            name == "LUNATE_UNICODE" ? "1" : baseEnvironment(name)
        );
        File.WriteAllText(host.Temp.File("a.txt"), "alpha beta");
        EnqueueScript(host);
        host.Client.Gate(1);
        host.Client.Gate(6);
        Task run = host.RunAsync();

        // Streaming paragraphs, a tool block, and mid-run steering injected before call 2.
        host.Console.SendText("first question");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        host.Console.SendText("steer mid-run");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.QueuedSteeringCount == 1);
        host.Client.Release(1);
        await host.Client.WaitForCallAsync(2);
        Assert.Equal("steer mid-run", host.Client.Requests[1][^1].Text);

        // Approval round trip: y for the first write, a (always) for the second; the third
        // write of the same tool runs without a prompt, provable by reaching call 5.
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);
        Assert.Equal("write", host.Session.PendingApproval!.ToolName);
        host.Console.SendText("y");
        await host.Client.WaitForCallAsync(3);
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);
        host.Console.SendText("a");
        await host.Client.WaitForCallAsync(5);

        Assert.Equal("one", File.ReadAllText(host.Temp.File("note.txt")));
        Assert.Equal("two", File.ReadAllText(host.Temp.File("b.txt")));
        Assert.Equal("three", File.ReadAllText(host.Temp.File("c.txt")));

        // Esc cancel returns the queued steering to the input line, unsent.
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.SendText("second question");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(6);
        host.Console.SendText("unsent steering");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.QueuedSteeringCount == 1);
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("unsent steering", StringComparison.Ordinal);
        });

        string finalFrame = Escape(host.LastFrame);

        // Clear the returned leftover, then complete and dispatch a command: /mod + Tab
        // completes to /model, Enter opens the picker, Down + Enter switches to gpt-4o.
        host.Console.SendCtrlC();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return !host.LastFrame.Contains("> unsent steering", StringComparison.Ordinal);
        });
        host.Console.SendText("/mod");
        host.Console.Send(new KeyEvent(KeyKind.Tab, null, false, false, false));
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("> /model", StringComparison.Ordinal);
        });
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("Select model", StringComparison.Ordinal);
        });
        string modelPickerFrame = Escape(host.LastFrame);

        host.Console.Send(new KeyEvent(KeyKind.Down, null, false, false, false));
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("> gpt-4o (openai)", StringComparison.Ordinal);
        });
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("model: gpt-4o", StringComparison.Ordinal);
        });

        // /new starts a fresh session; /resume lists the directory (the original id maps to
        // <old-session> and the new one to <new-session>) with the current (newest) session
        // selected first.
        string oldId = Path.GetFileNameWithoutExtension(
            Assert.Single(Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl"))
        );
        host.Console.SendText("/new");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("new session", StringComparison.Ordinal);
        });
        string[] sessionFiles = Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl");
        Assert.Equal(2, sessionFiles.Length);
        string newId = Path.GetFileNameWithoutExtension(
            sessionFiles.Single(path =>
                !string.Equals(
                    Path.GetFileNameWithoutExtension(path),
                    oldId,
                    StringComparison.Ordinal
                )
            )
        );
        host.Console.SendText("/resume");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("Select session", StringComparison.Ordinal);
        });
        string sessionPickerFrame = Escape(NormalizeSessionIds(host.LastFrame, oldId, newId));

        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return NormalizeSessionIds(host.LastFrame, oldId, newId)
                .Contains("resumed <new-session>", StringComparison.Ordinal);
        });

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;

        InteractiveGoldens.AssertMatchesText("end-to-end-scrollback.txt", host.ScrollbackText);
        InteractiveGoldens.AssertMatchesText("end-to-end-final-frame.txt", finalFrame);
        InteractiveGoldens.AssertMatchesText("end-to-end-model-picker-frame.txt", modelPickerFrame);
        InteractiveGoldens.AssertMatchesText(
            "end-to-end-session-picker-frame.txt",
            sessionPickerFrame
        );
    }

    /// <summary>Maps the original session's full id to <c>&lt;old-session&gt;</c> and the
    /// /new-created session's id to <c>&lt;new-session&gt;</c>; the ids are stamped with the wall
    /// clock and random bytes, so the golden must not depend on them.</summary>
    private static string NormalizeSessionIds(string text, string oldId, string newId)
    {
        text = text.Replace(oldId, "<old-session>", StringComparison.Ordinal);
        return text.Replace(newId, "<new-session>", StringComparison.Ordinal);
    }

    private static void EnqueueScript(InteractiveSessionHost host)
    {
        host.Client.Enqueue(
            Scripts.Text("para one\n\npara two"),
            Scripts.Call("call-1", "read", Scripts.Args(("path", "a.txt"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(
            Scripts.Call("call-2", "write", Scripts.Args(("path", "note.txt"), ("content", "one"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(
            Scripts.Call("call-3", "write", Scripts.Args(("path", "b.txt"), ("content", "two"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(
            Scripts.Call("call-4", "write", Scripts.Args(("path", "c.txt"), ("content", "three"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("all done"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("never"), Scripts.Stop());
    }

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\")
            .Replace(EscapeNext, "\\x1b")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
}
