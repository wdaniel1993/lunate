using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The session's activity-line wiring: the run baseline turns cumulative usage into the current
/// run's deltas, a run without usage shows the hint but no deltas, a second run starts from zero
/// again, and the active tool's argument summary reaches the live frame.
/// </summary>
public sealed class InteractiveSessionActivityTests
{
    [Fact]
    public async Task A_gated_first_call_shows_working_without_deltas()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("working…", StringComparison.Ordinal);
        });

        Assert.DoesNotContain("↑", host.LastFrame, StringComparison.Ordinal);
        Assert.Contains("esc to interrupt", host.LastFrame, StringComparison.Ordinal);

        host.Client.Release(1);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Advance(33);
        Assert.DoesNotContain("working…", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("esc to interrupt", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_first_usage_event_shows_the_run_deltas()
    {
        var gate = new TextUpdateGate("paused");
        using var host = new InteractiveSessionHost(configureHarness: options =>
            options with
            {
                Hooks = gate,
            }
        );
        host.Client.Enqueue(Usage(1000, 500), Scripts.Text("paused"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("go");
        host.Console.SendEnter();
        await gate.Reached.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken
        );
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("↑1.0k ↓500", StringComparison.Ordinal);
        });

        Assert.Contains("working…", host.LastFrame, StringComparison.Ordinal);
        Assert.Contains("esc to interrupt", host.LastFrame, StringComparison.Ordinal);

        gate.Release();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_second_run_baselines_from_zero_again()
    {
        var gate = new TextUpdateGate("paused");
        using var host = new InteractiveSessionHost(configureHarness: options =>
            options with
            {
                Hooks = gate,
            }
        );
        host.Client.Enqueue(Usage(1000, 500), Scripts.Text("first"), Scripts.Stop());
        host.Client.Enqueue(Usage(10, 5), Scripts.Text("paused"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("one");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
            host.ScrollbackText.Contains("first", StringComparison.Ordinal)
        );
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        host.Console.SendText("two");
        host.Console.SendEnter();
        await gate.Reached.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken
        );
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("↑10 ↓5", StringComparison.Ordinal);
        });

        Assert.DoesNotContain("↑1.0k", host.LastFrame, StringComparison.Ordinal);

        gate.Release();
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_live_frame_shows_the_active_tools_argument_summary()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Session.HandleEvent(new ToolCallStart("run", "call_1", "read"));
        host.Session.HandleEvent(new ToolCallArgs("run", "call_1", """{"path":"src/foo.cs"}"""));
        host.Advance(33);

        Assert.Contains("| read src/foo.cs · 0s", host.LastFrame, StringComparison.Ordinal);

        host.Session.HandleEvent(new ToolCallResult("run", "call_1", "ok", false));
        host.Advance(33);
        Assert.DoesNotContain("read src/foo.cs", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    private static ChatResponseUpdate Usage(long input, long output) =>
        new(
            ChatRole.Assistant,
            [
                new UsageContent(
                    new UsageDetails { InputTokenCount = input, OutputTokenCount = output }
                ),
            ]
        );

    /// <summary>
    /// Pauses the provider stream on a named text update, after the preceding usage update has
    /// been handled, so the live frame can be sampled deterministically.
    /// </summary>
    private sealed class TextUpdateGate(string pauseOn) : IAgentHookPoints
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public TaskCompletionSource Reached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ProviderStreamEventAsync(
            string runId,
            string? parentRunId,
            ChatResponseUpdate update,
            CancellationToken cancellationToken
        )
        {
            if (update.Contents.OfType<TextContent>().Any(content => content.Text == pauseOn))
            {
                Reached.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
        }

        public void Release() => _release.TrySetResult();
    }
}
