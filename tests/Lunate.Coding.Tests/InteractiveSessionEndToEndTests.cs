using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Lunate.Agent;
using Lunate.TestSupport;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>Golden files for the scripted end-to-end snapshot, regenerated deliberately.</summary>
internal static class InteractiveGoldens
{
    public static bool UpdateRequested =>
        Environment.GetEnvironmentVariable("LUNATE_CODING_UPDATE_GOLDENS") == "1";

    public static string Directory =>
        Path.Combine(
            RepositoryRoot.Find(),
            "tests",
            "Lunate.Coding.Tests",
            "fixtures",
            "interactive"
        );

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
        var steering = new SteeringQueue();
        using var host = new InteractiveSessionHost(
            environment: name =>
                name switch
                {
                    "LUNATE_UNICODE" => "1",
                    // The welcome header's normal form needs a resolving provider; the scripted
                    // provider never uses the key.
                    "OPENAI_API_KEY" => "test-key",
                    _ => baseEnvironment(name),
                },
            configureHarness: options => options with { Steering = steering }
        );
        // The header prints the workspace path; a console narrower than that path would wrap it,
        // so the scrollback console is widened and the path is normalized out of the golden.
        host.Scrollback.Profile.Width = 200;
        File.WriteAllText(host.Temp.File("a.txt"), "alpha beta");
        EnqueueScript(host);
        host.Client.Gate(1);
        host.Client.Gate(6);
        Task run = host.RunAsync();

        // Streaming paragraphs, a tool block, and mid-run steering injected through the harness
        // API before call 2.
        host.Console.SendText("first question");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        steering.Enqueue("steer mid-run");
        host.Client.Release(1);
        await host.Client.WaitForCallAsync(2);
        Assert.Equal("steer mid-run", host.Client.Requests[1][^1].Text);

        // Approval round trip: y for the first write, a (always) for the second; the third
        // write of the same tool runs without a prompt, provable by reaching call 5.
        //
        // This block is the one that fails on a loaded leg: a timeout here says nothing about WHY
        // the session stopped, so the failure carries the session's own state. Without it the next
        // red leg costs another fifteen-minute CI cycle to guess at.
        try
        {
            await host.WaitForApprovalAsync();
            Assert.Equal("write", host.Session.PendingApproval!.ToolName);
            host.Console.SendText("y");
            await host.Client.WaitForCallAsync(3);
            await host.WaitForApprovalAsync();
            host.Console.SendText("a");
            await host.Client.WaitForCallAsync(5);
        }
        catch (TimeoutException ex)
        {
            Assert.Fail(
                $"""
                {ex.Message}
                --- session state ---
                running={host.Session.IsRunning}
                pendingApproval={host.Session.PendingApproval?.ToolName ?? "none"}
                queued={host.Session.FollowUps.Count}
                requests={host.Client.Requests.Count}
                lastFrame={Tail(host.LastFrame, 240)}
                """
            );
        }

        Assert.Equal("one", File.ReadAllText(host.Temp.File("note.txt")));
        Assert.Equal("two", File.ReadAllText(host.Temp.File("b.txt")));
        Assert.Equal("three", File.ReadAllText(host.Temp.File("c.txt")));

        // Esc cancels the second turn; the text left in the editor stays unsubmitted.
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.SendText("second question");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(6);
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.SendText("unsent steering");
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("unsent steering", StringComparison.Ordinal);
        });

        string finalFrame = await CaptureFrameAsync(host);

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
        // The catalog is large, so the scripted filter selects gpt-4o before it confirms.
        host.Console.SendText("gpt-4o (");
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("filter: gpt-4o (", StringComparison.Ordinal)
                && host.LastFrame.Contains("> gpt-4o (openai)", StringComparison.Ordinal);
        });
        string modelPickerFrame = await CaptureFrameAsync(host);

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
        // The notice is printed after the file is created, but the directory read is only
        // eventually consistent on NTFS under a scanning agent: wait for the second file rather
        // than assume the move is visible in the same frame. A missing file still fails, with the
        // directory contents in the message.
        await host.WaitUntilAsync(
            () =>
            {
                host.Advance(33);
                return Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl").Length == 2;
            },
            TimeSpan.FromSeconds(10)
        );
        string[] sessionFiles = Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl");
        Assert.True(
            sessionFiles.Length == 2,
            "sessions: " + string.Join(", ", sessionFiles.Select(Path.GetFileName))
        );
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
        string sessionPickerFrame = NormalizeSessionIds(
            await CaptureFrameAsync(host),
            oldId,
            newId
        );

        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return NormalizeSessionIds(host.LastFrame, oldId, newId)
                .Contains("resumed <new-session>", StringComparison.Ordinal);
        });

        host.Console.SendText("/quit");
        host.Console.SendEnter();

        // The scrollback golden is the other half of this test's flake: when the steer echo is
        // missing the diff cannot say whether the message never reached the model or reached it and
        // was never echoed. This block answers that in the failure message. The golden is read
        // after the run completed, so it pins the whole session's commits, not a live prefix.
        string scrollback = NormalizeWorkspace(host, await host.ScrollbackAfterAsync(() => run));
        try
        {
            InteractiveGoldens.AssertMatchesText("end-to-end-scrollback.txt", scrollback);
        }
        catch (Exception ex)
        {
            Assert.Fail(
                $"""
                {ex.Message}
                --- steering state ---
                steeringQueued={steering.Count}
                steeringInjectedEvents={host.Events.OfType<SteeringInjected>().Count()}
                steerEchoIndex={scrollback.IndexOf("» steer mid-run", StringComparison.Ordinal)}
                requestLastTexts={string.Join(" | ", host.Client.Requests.Select(r => r[^1].Text))}
                """
            );
        }
        InteractiveGoldens.AssertMatchesText("end-to-end-final-frame.txt", finalFrame);
        InteractiveGoldens.AssertMatchesText("end-to-end-model-picker-frame.txt", modelPickerFrame);
        InteractiveGoldens.AssertMatchesText(
            "end-to-end-session-picker-frame.txt",
            sessionPickerFrame
        );
    }

    /// <summary>
    /// Waits for the live frame to stop changing and returns it in a history-free canonical
    /// form. The raw write also moves over the previous frame's height, which depends on how
    /// many frames the session managed to paint before the test observed this one; pinning
    /// those bytes makes the golden timing-dependent. The canonical form pins what the frame
    /// draws (its lines and cursor), not the overpaint artifacts.
    /// </summary>
    private static async Task<string> CaptureFrameAsync(InteractiveSessionHost host)
    {
        string previous = string.Empty;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            host.Advance(33);
            await Task.Delay(1, TestContext.Current.CancellationToken);
            string current = Escape(CanonicalFrame(host.LastFrame));
            if (attempt > 0 && string.Equals(current, previous, StringComparison.Ordinal))
            {
                return current;
            }

            previous = current;
        }

        return previous;
    }

    /// <summary>
    /// A FrameWriter paint in canonical form: the written lines and the cursor, with the
    /// leading carry-over move and the trailing clears of a taller previous frame removed.
    /// </summary>
    private static string CanonicalFrame(string write)
    {
        string[] rows = write.Split("\r\u001b[2K");
        if (rows.Length == 1)
        {
            return write;
        }

        string last = rows[^1];
        var column = 0;
        Match right = Regex.Match(last, "\\r(?:\\x1b\\[(\\d+)C)?$");
        if (right.Success)
        {
            if (right.Groups[1].Success)
            {
                column = int.Parse(right.Groups[1].Value, CultureInfo.InvariantCulture);
            }

            last = last[..right.Index];
        }

        var up = 0;
        Match upper = Regex.Match(last, "\\x1b\\[(\\d+)A$");
        if (upper.Success)
        {
            up = int.Parse(upper.Groups[1].Value, CultureInfo.InvariantCulture);
            last = last[..upper.Index];
        }

        var lines = new List<string>(rows.Length - 1);
        for (var index = 1; index < rows.Length; index++)
        {
            string text = index == rows.Length - 1 ? last : rows[index];
            lines.Add(text.EndsWith('\n') ? text[..^1] : text);
        }

        int rowCount = lines.Count;
        var cursorRow = rowCount - up;
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var builder = new StringBuilder();
        foreach (string line in lines)
        {
            builder.Append("\r\u001b[2K").Append(line).Append('\n');
        }

        int trail = lines.Count - cursorRow;
        if (trail > 0)
        {
            builder.Append("\u001b[").Append(trail).Append('A');
        }

        builder.Append('\r');
        if (column > 0)
        {
            builder.Append("\u001b[").Append(column).Append('C');
        }

        return builder.ToString();
    }

    /// <summary>Maps the original session's full id to <c>&lt;old-session&gt;</c> and the
    /// /new-created session's id to <c>&lt;new-session&gt;</c>; the ids are stamped with the wall
    /// clock and random bytes, so the golden must not depend on them.</summary>
    private static string NormalizeSessionIds(string text, string oldId, string newId)
    {
        text = text.Replace(oldId, "<old-session>", StringComparison.Ordinal);
        return text.Replace(newId, "<new-session>", StringComparison.Ordinal);
    }

    /// <summary>The welcome header prints the workspace path; it is machine-specific, so the golden
    /// carries <c>&lt;workspace&gt;</c> instead. The session prints the display form, which is
    /// <c>~</c>-shortened when the workspace is under the user's home (Windows CI temp
    /// directories are), so the full path and its display form are both normalized.</summary>
    private static string NormalizeWorkspace(InteractiveSessionHost host, string text)
    {
        string workspace = Workspace.Canonicalize(host.Temp.Root);
        return NormalizeBuild(
            text.Replace(workspace, "<workspace>", StringComparison.Ordinal)
                .Replace(DisplayPath.For(workspace), "<workspace>", StringComparison.Ordinal)
        );
    }

    // The header names the running build, and the build hash changes with every commit. The golden
    // pins the shape, not a hash that would be stale the moment it is committed.
    private static string Tail(string text, int length) =>
        text.Length <= length ? text : text[^length..];

    private static string NormalizeBuild(string text) =>
        BuildToken.Replace(text, "v<version>+<build>");

    private static readonly Regex BuildToken = new(
        // SemVer puts an arbitrary prerelease after a hyphen: 0.1.0-rc.1 must normalise
        // like the stable version does.
        @"\bv\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?\+[0-9a-fA-F]{7}\b",
        RegexOptions.Compiled
    );

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
