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
            WaitBudgets.InProcess,
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
        await host.WaitForScrollbackAsync("first");
        await host.WaitUntilAsync(() => !host.Session.IsRunning);

        host.Console.SendText("two");
        host.Console.SendEnter();
        await gate.Reached.Task.WaitAsync(
            WaitBudgets.InProcess,
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

    [Fact]
    public async Task A_scripted_run_shows_the_thinking_line_then_the_tool_activity_line()
    {
        var gate = new ActivityGate();
        using var host = new InteractiveSessionHost(configureHarness: options =>
            options with
            {
                Hooks = gate,
            }
        );
        File.WriteAllText(host.Temp.File("notes.txt"), "line one\nline two");
        host.Client.Enqueue(
            Usage(1200, 180),
            Scripts.Reasoning("Let me "),
            Scripts.Reasoning("weigh the options."),
            Scripts.Call("call-1", "read", Scripts.Args(("path", "notes.txt"))),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());

        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        host.Console.SendText("Read notes.txt");
        host.Console.SendEnter();

        await gate.ReasoningReached.Task.WaitAsync(
            WaitBudgets.InProcess,
            TestContext.Current.CancellationToken
        );
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("⠹ thinking…", StringComparison.Ordinal);
        });

        string thinking = host.LastFrame;
        Assert.Contains("↑1.2k ↓180", thinking, StringComparison.Ordinal);
        Assert.Contains("esc to interrupt", thinking, StringComparison.Ordinal);

        gate.ReleaseReasoning();
        await gate.ToolReached.Task.WaitAsync(
            WaitBudgets.InProcess,
            TestContext.Current.CancellationToken
        );
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains("read notes.txt", StringComparison.Ordinal);
        });

        string tool = host.LastFrame;
        Assert.Contains("↑1.2k ↓180", tool, StringComparison.Ordinal);
        Assert.Contains("esc to interrupt", tool, StringComparison.Ordinal);
        Assert.DoesNotContain("thinking…", tool, StringComparison.Ordinal);

        gate.ReleaseTool();
        await host.WaitForScrollbackAsync(
            text => text.Contains("done", StringComparison.Ordinal) && !host.Session.IsRunning,
            "the answer once the turn finishes"
        );

        Assert.Empty(EventSequenceValidator.Validate(host.Events));

        host.Console.SendText("/quit");
        host.Console.SendEnter();
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
    /// Pauses the provider stream on the second reasoning fragment and the harness on the read
    /// call: the live thinking line and then the live tool line can each be sampled before the
    /// run moves on.
    /// </summary>
    private sealed class ActivityGate : IAgentHookPoints
    {
        private readonly TaskCompletionSource _releaseReasoning = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _releaseTool = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public TaskCompletionSource ReasoningReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ToolReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ProviderStreamEventAsync(
            string runId,
            string? parentRunId,
            ChatResponseUpdate update,
            CancellationToken cancellationToken
        )
        {
            if (
                update
                    .Contents.OfType<TextReasoningContent>()
                    .Any(content => content.Text == "weigh the options.")
            )
            {
                ReasoningReached.TrySetResult();
                await _releaseReasoning.Task.WaitAsync(cancellationToken);
            }
        }

        public async ValueTask<AgentToolCallingResult> ToolCallingAsync(
            AgentToolCallingContext context,
            CancellationToken cancellationToken
        )
        {
            if (context.ToolName == "read")
            {
                ToolReached.TrySetResult();
                await _releaseTool.Task.WaitAsync(cancellationToken);
            }

            return new AgentToolCallingResult.Proceed(context.Arguments);
        }

        public void ReleaseReasoning() => _releaseReasoning.TrySetResult();

        public void ReleaseTool() => _releaseTool.TrySetResult();
    }

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
