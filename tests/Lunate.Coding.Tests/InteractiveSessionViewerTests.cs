using Lunate.Agent;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The Ctrl+O transcript viewer at the session seam: opened through the key pump over the full
/// transcript, refused while a prompt or picker owns input, modal while open — keys never reach
/// the model — and closed with the scrollback unchanged and the alternate screen restored.
/// </summary>
public sealed class InteractiveSessionViewerTests
{
    [Fact]
    public async Task CtrlO_opens_the_viewer_with_the_full_transcript()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await CommitBuildBlockAsync(host);

        host.Console.SendCtrlO();
        await host.WaitUntilAsync(() => host.Console.InAlternateScreen);
        await host.WaitUntilAsync(() =>
            host.Console.Writes.Any(write =>
                write.Contains("step 30 finished", StringComparison.Ordinal)
            )
        );

        Assert.Contains("\u001b[?1049h", host.Frames, StringComparison.Ordinal);
        Assert.Equal(1, host.Console.AlternateScreenEntries);
        // The live area is suspended while the viewer owns the terminal.
        int painted = host.Console.Writes.Count;
        host.Advance(200);
        Assert.Equal(painted, host.Console.Writes.Count);

        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Console.InAlternateScreen);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Keys_while_open_never_reach_the_model()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendCtrlO();
        await host.WaitUntilAsync(() => host.Console.InAlternateScreen);

        host.Console.SendText("summary");
        host.Console.SendEnter();
        host.Advance(100);

        Assert.Empty(host.Client.Requests);
        Assert.False(host.Session.IsRunning);
        Assert.Empty(host.Session.Transcript);

        // The typed keys never entered the input line: the first submission is exactly "hi".
        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Console.InAlternateScreen);
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        Assert.Equal(
            "hi",
            host.Client.Requests[0].Single(message => message.Role == ChatRole.User).Text
        );

        host.Console.Complete();
        await run;
    }

    [Theory]
    [InlineData(KeyKind.Character, "o", true)]
    [InlineData(KeyKind.Character, "q", false)]
    [InlineData(KeyKind.Escape, null, false)]
    public async Task Closing_keys_leave_the_screen_and_keep_the_scrollback(
        KeyKind kind,
        string? text,
        bool ctrl
    )
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        string scrollback = host.ScrollbackText;
        host.Console.SendCtrlO();
        await host.WaitUntilAsync(() => host.Console.InAlternateScreen);

        host.Console.Send(new KeyEvent(kind, text, ctrl, false, false));
        await host.WaitUntilAsync(() => !host.Console.InAlternateScreen);

        Assert.Equal("\u001b[?1049l", host.Console.Writes[^1]);
        Assert.Equal(scrollback, host.ScrollbackText);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task CtrlO_is_refused_while_a_picker_is_open()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();

        host.Console.Send(new KeyEvent(KeyKind.Character, "p", false, false, true));
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "Select model");

        host.Console.SendCtrlO();
        host.Advance(100);

        Assert.False(host.Console.InAlternateScreen);
        Assert.Equal(0, host.Console.AlternateScreenEntries);

        host.Console.SendEscape();
        await InteractiveSessionCommandTests.WaitForFrameGoneAsync(host, "Select model");
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task CtrlO_is_refused_while_an_approval_prompt_is_open()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(
            Scripts.Call("call-1", "write", Scripts.Args(("path", "note.txt"), ("content", "hi"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.PendingApproval is not null);

        host.Console.SendCtrlO();
        host.Advance(100);

        Assert.False(host.Console.InAlternateScreen);
        Assert.Equal(0, host.Console.AlternateScreenEntries);

        host.Console.SendText("y");
        await host.Client.WaitForCallAsync(2);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Ending_the_session_with_the_viewer_open_leaves_the_screen()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendCtrlO();
        await host.WaitUntilAsync(() => host.Console.InAlternateScreen);

        // The key stream ends with the viewer open: the session's exit path must restore.
        host.Console.Complete();
        await run;

        Assert.False(host.Console.InAlternateScreen);
        Assert.Equal("\u001b[?1049l", host.Console.Writes[^1]);
    }

    [Fact]
    public async Task Output_committed_while_open_lands_in_the_scrollback_after_close()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendCtrlO();
        await host.WaitUntilAsync(() => host.Console.InAlternateScreen);

        string duringOpen = host.ScrollbackText;
        host.Session.HandleEvent(new RunStarted("run"));
        host.Session.HandleEvent(new TextMessageContent("run", "m1", "while you were looking"));
        host.Session.HandleEvent(new TextMessageEnd("run", "m1"));
        host.Advance(40);

        // The commit queued: the live area is suspended below the alternate screen.
        Assert.Equal(duringOpen, host.ScrollbackText);

        host.Console.SendEscape();
        await host.WaitUntilAsync(() =>
            host.ScrollbackText.Contains("while you were looking", StringComparison.Ordinal)
        );

        Assert.Contains("while you were looking", host.ScrollbackText, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_failing_screen_entry_resumes_the_live_area()
    {
        using var host = new InteractiveSessionHost();
        host.Console.FailAlternateScreen = true;
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.Session.HandleKeyAsync(
                new KeyEvent(KeyKind.Character, "o", true, false, false),
                sequence: 1,
                TestContext.Current.CancellationToken
            )
        );

        Assert.False(host.Console.InAlternateScreen);

        // The live area was not left suspended: the typed key paints a frame again.
        int painted = host.Console.Writes.Count;
        await host.Session.HandleKeyAsync(
            new KeyEvent(KeyKind.Character, "x", false, false, false),
            sequence: 2,
            TestContext.Current.CancellationToken
        );
        host.Advance(100);

        Assert.True(host.Console.Writes.Count > painted);
        Assert.Contains("> x", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_welcome_footer_names_the_viewer_binding()
    {
        using var host = new InteractiveSessionHost(environment: KeyedEnvironment());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        Assert.Contains("ctrl+o transcript", host.ScrollbackText, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    /// <summary>A provider key resolves, so the header prints its full block with the hint line.</summary>
    private static Func<string, string?> KeyedEnvironment() =>
        Environment(("LUNATE_UNICODE", "1"), ("OPENAI_API_KEY", "test-key"));

    private static Func<string, string?> Environment(params (string Name, string Value)[] overrides)
    {
        Func<string, string?> baseline = PrintModeTestSupport.Environment();
        return name =>
        {
            foreach ((string overridden, string value) in overrides)
            {
                if (string.Equals(name, overridden, StringComparison.Ordinal))
                {
                    return value;
                }
            }

            return baseline(name);
        };
    }

    [Fact]
    public async Task Navigation_keys_scroll_the_viewer_through_the_key_pump()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await CommitBuildBlockAsync(host);

        host.Console.SendCtrlO();
        await host.WaitUntilAsync(() => host.Console.InAlternateScreen);

        host.Console.Send(new KeyEvent(KeyKind.Up, null, false, false, false));
        await host.WaitUntilAsync(() =>
            host.Console.Writes[^1].Contains("step 29 finished", StringComparison.Ordinal)
        );

        // A page at 80x24 is 23 lines and the document (header + 30 lines) is 31, so PageUp
        // clamps at the top: the header and the first output line are on screen.
        host.Console.Send(new KeyEvent(KeyKind.PageUp, null, false, false, false));
        await host.WaitUntilAsync(() =>
            host.Console.Writes[^1].Contains("step 01 finished", StringComparison.Ordinal)
        );

        host.Console.Send(new KeyEvent(KeyKind.Character, "G", false, false, false));
        await host.WaitUntilAsync(() =>
            host.Console.Writes[^1].Contains("step 30 finished", StringComparison.Ordinal)
        );

        host.Console.Complete();
        await run;
    }

    /// <summary>A finished bash block whose output is taller than the viewer's frame.</summary>
    private static async Task CommitBuildBlockAsync(InteractiveSessionHost host)
    {
        string output = string.Join(
            '\n',
            Enumerable.Range(1, 30).Select(n => $"step {n:00} finished")
        );
        host.Session.HandleEvent(new RunStarted("run"));
        host.Session.HandleEvent(new ToolCallStart("run", "call_1", "bash"));
        host.Session.HandleEvent(new ToolCallArgs("run", "call_1", """{"command":"build"}"""));
        host.Session.HandleEvent(
            new ToolCallResult("run", "call_1", output, false, new BashDetails(0))
        );
        host.Advance(40);
        await host.WaitUntilAsync(() => host.Session.Transcript.Count == 1);
    }
}
