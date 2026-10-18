using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The interactive pump with reasoning events: the session's own run streams into the live
/// thinking block and clears it when the reasoning ends; a child run's reasoning never reaches
/// the block.
/// </summary>
public sealed class InteractiveSessionReasoningTests
{
    [Fact]
    public async Task The_parent_runs_reasoning_renders_live_and_clears_on_the_end()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Session.HandleEvent(new ReasoningStart("run_parent", "msg_r"));
        host.Advance(40);
        Assert.Contains("⠹ thinking… 0s", host.LastFrame, StringComparison.Ordinal);

        host.Session.HandleEvent(
            new ReasoningMessageContent("run_parent", "msg_r", "weighing the options")
        );
        host.Advance(40);
        Assert.Contains("weighing the options", host.LastFrame, StringComparison.Ordinal);

        host.Session.HandleEvent(new ReasoningEnd("run_parent", "msg_r"));
        host.Advance(40);
        Assert.DoesNotContain("thinking…", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("weighing the options", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_child_runs_reasoning_never_reaches_the_live_block()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Session.HandleEvent(new RunStarted("run_parent"));
        host.Session.HandleEvent(new RunStarted("run_child") { ParentRunId = "run_parent" });
        host.Session.HandleEvent(new ReasoningStart("run_parent", "msg_p"));
        host.Session.HandleEvent(
            new ReasoningMessageContent("run_parent", "msg_p", "parent weighing")
        );
        host.Advance(40);
        Assert.Contains("parent weighing", host.LastFrame, StringComparison.Ordinal);

        host.Session.HandleEvent(
            new ReasoningStart("run_child", "msg_c") { ParentRunId = "run_parent" }
        );
        host.Session.HandleEvent(
            new ReasoningMessageContent("run_child", "msg_c", "secret child reasoning")
            {
                ParentRunId = "run_parent",
            }
        );
        host.Advance(40);
        Assert.Contains("parent weighing", host.LastFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("secret child reasoning", host.LastFrame, StringComparison.Ordinal);

        // The child's end must not clear the parent's block either.
        host.Session.HandleEvent(
            new ReasoningEnd("run_child", "msg_c") { ParentRunId = "run_parent" }
        );
        host.Advance(40);
        Assert.Contains("parent weighing", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }
}
