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
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken
        );

        // The session drains the harness's event channel on its own task: once the provider is
        // parked, the earlier ToolCallResult is queued and the block commits while it stays
        // parked. Wait for that commit, then assert the turn is still active.
        await host.WaitUntilAsync(() =>
            host.ScrollbackText.Contains(Header, StringComparison.Ordinal)
        );
        string midRunScrollback = host.ScrollbackText;
        Assert.True(host.Session.IsRunning);
        Assert.DoesNotContain(LastLine, midRunScrollback, StringComparison.Ordinal);
        gate.Release();

        await host.WaitUntilAsync(() =>
            !host.Session.IsRunning
            && host.ScrollbackText.Contains("done", StringComparison.Ordinal)
        );
        string scrollback = host.ScrollbackText;

        host.Console.SendCtrlO();
        await host.WaitUntilAsync(() => host.Console.InAlternateScreen);
        // The viewer opens on the latest content: the tail of the full read output, which the
        // scrollback never showed, plus the key footer.
        await host.WaitUntilAsync(() =>
            host.Console.Writes.Any(write =>
                write.Contains("line 20 of the file", StringComparison.Ordinal)
                && write.Contains(LastLine, StringComparison.Ordinal)
                && write.Contains("scroll", StringComparison.Ordinal)
                && write.Contains("q close", StringComparison.Ordinal)
            )
        );
        Assert.Equal(scrollback, host.ScrollbackText);

        host.Console.SendEscape();
        await host.WaitUntilAsync(() => !host.Console.InAlternateScreen);
        Assert.Equal("\u001b[?1049l", host.Console.Writes[^1]);
        Assert.Equal(scrollback, host.ScrollbackText);

        Assert.Empty(EventSequenceValidator.Validate(host.Events));
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
