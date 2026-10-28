using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The production-composition guard for tool blocks: the real host runs a scripted turn whose
/// <c>read</c> call commits the compact one-line block while the run is still in flight, and
/// <c>Ctrl+O</c> opens the viewer on the full output the scrollback elided. Closing the viewer
/// restores the alternate screen and leaves the scrollback byte-for-byte unchanged; the run's own
/// event stream is validator-clean.
/// </summary>
public sealed class InteractiveSessionToolBlocksCompositionTests
{
    private const string Header = "✓ read  notes.txt (30 lines)";
    private const string LastLine = "line 30 of the file";

    [Fact]
    public async Task A_read_call_commits_a_compact_block_and_CtrlO_opens_the_full_output()
    {
        var gate = new FinalTextGate();
        using var host = new InteractiveSessionHost(configureHarness: options =>
            options with
            {
                Hooks = gate,
            }
        );
        File.WriteAllText(
            host.Temp.File("notes.txt"),
            string.Join('\n', Enumerable.Range(1, 30).Select(n => $"line {n:00} of the file"))
        );
        host.Client.Enqueue(
            Scripts.Call("call-1", "read", Scripts.Args(("path", "notes.txt"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());

        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Console.SendText("go");
        host.Console.SendEnter();

        // The harness awaits the provider-stream hook per update, so the second stream parks on
        // its final "done" update: the read block has committed while the turn is still active.
        await gate.Reached.Task.WaitAsync(
            WaitBudgets.InProcess,
            TestContext.Current.CancellationToken
        );

        // The session drains the harness's event channel on its own task: once the provider is
        // parked, the earlier ToolCallResult is queued and the block commits while it stays
        // parked. Wait for that commit, then assert the turn is still active.
        string midRunScrollback = await host.WaitForScrollbackAsync(Header);
        Assert.True(host.Session.IsRunning);
        Assert.DoesNotContain(LastLine, midRunScrollback, StringComparison.Ordinal);
        gate.Release();

        string scrollback = await host.WaitForScrollbackAsync(
            text => text.Contains("done", StringComparison.Ordinal) && !host.Session.IsRunning,
            "the answer once the turn finishes"
        );

        host.Console.SendCtrlO();
        await host.WaitUntilAsync(() => host.Console.InAlternateScreen);
        // The viewer opens on the latest content: the tail of the full read output, which the
        // scrollback never showed, plus the key footer. Reading only after that frame completed
        // is what makes "the scrollback did not change" a statement about the open surface.
        Assert.Equal(
            scrollback,
            await host.ScrollbackAfterAsync(() =>
                host.WaitUntilAsync(() =>
                    host.Console.Writes.Any(write =>
                        write.Contains("line 20 of the file", StringComparison.Ordinal)
                        && write.Contains(LastLine, StringComparison.Ordinal)
                        && write.Contains("scroll", StringComparison.Ordinal)
                        && write.Contains("q close", StringComparison.Ordinal)
                    )
                )
            )
        );

        host.Console.SendEscape();
        Assert.Equal(
            scrollback,
            await host.ScrollbackAfterAsync(() =>
                host.WaitUntilAsync(() => !host.Console.InAlternateScreen)
            )
        );
        Assert.Equal("\u001b[?1049l", host.Console.Writes[^1]);

        Assert.Empty(EventSequenceValidator.Validate(host.Events));
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_bash_call_commits_the_header_and_the_ctrl_o_hint_without_its_output()
    {
        using var host = new InteractiveSessionHost();
        File.WriteAllText(host.Temp.File("bash-notes.txt"), "hidden tool output");
        host.Client.Enqueue(
            Scripts.Call("call-1", "bash", Scripts.Args(("command", "cat bash-notes.txt"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());

        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitForApprovalAsync();
        Assert.Equal("bash", host.Session.PendingApproval!.ToolName);
        host.Console.SendText("y");
        string scrollback = await host.WaitForScrollbackAsync(
            text => text.Contains("done", StringComparison.Ordinal) && !host.Session.IsRunning,
            "the answer once the turn finishes"
        );

        string[] lines = scrollback.Split('\n');
        string header = Assert.Single(
            lines,
            line => line.StartsWith("✓ bash  cat bash-notes.txt", StringComparison.Ordinal)
        );
        Assert.Contains("ctrl+o", header, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden tool output", scrollback, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    /// <summary>
    /// Pauses the provider stream on the second script's final text: the harness awaits the hook
    /// for every update, so the turn stays mid-flight until the test releases it.
    /// </summary>
    private sealed class FinalTextGate : IAgentHookPoints
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        /// <summary>Completes once the stream reached the second script's "done" update.</summary>
        public TaskCompletionSource Reached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ProviderStreamEventAsync(
            string runId,
            string? parentRunId,
            ChatResponseUpdate update,
            CancellationToken cancellationToken
        )
        {
            if (update.Contents.OfType<TextContent>().Any(content => content.Text == "done"))
            {
                Reached.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
        }

        public void Release() => _release.TrySetResult();
    }
}
