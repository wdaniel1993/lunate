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
        using var host = new InteractiveSessionHost();
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

        // Quit: the non-empty input clears, the next press arms, the third quits.
        host.Console.SendCtrlC();
        host.Console.SendCtrlC();
        host.Console.SendCtrlC();
        await run;

        InteractiveGoldens.AssertMatchesText("end-to-end-scrollback.txt", host.ScrollbackText);
        InteractiveGoldens.AssertMatchesText("end-to-end-final-frame.txt", finalFrame);
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
