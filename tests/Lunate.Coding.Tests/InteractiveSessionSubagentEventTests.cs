using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The interactive pump with subagent events: child text and child tool blocks stay out of
/// scrollback, the live area shows the subagent status, keys stay scoped by (RunId, CallId) and
/// child usage still rolls into the footer.
/// </summary>
public sealed class InteractiveSessionSubagentEventTests
{
    [Fact]
    public async Task Child_events_are_suppressed_scoped_and_rolled_up()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Session.HandleEvent(new RunStarted("run_parent"));
        host.Session.HandleEvent(new RunStarted("run_child") { ParentRunId = "run_parent" });
        host.Advance(40);
        Assert.Contains("subagent running", host.LastFrame, StringComparison.Ordinal);

        host.Session.HandleEvent(
            new TextMessageContent("run_child", "msg_c", "streamed secret")
            {
                ParentRunId = "run_parent",
            }
        );
        host.Session.HandleEvent(
            new ToolCallStart("run_child", "call_1", "read") { ParentRunId = "run_parent" }
        );
        host.Session.HandleEvent(
            new ToolCallArgs("run_child", "call_1", """{"path":"secret.txt"}""")
            {
                ParentRunId = "run_parent",
            }
        );
        host.Session.HandleEvent(
            new ToolCallEnd("run_child", "call_1") { ParentRunId = "run_parent" }
        );
        host.Session.HandleEvent(
            new ToolCallResult("run_child", "call_1", "secret child output", IsError: false)
            {
                ParentRunId = "run_parent",
            }
        );
        host.Session.HandleEvent(
            new UsageUpdated(
                "run_child",
                new UsageDetails { InputTokenCount = 7, OutputTokenCount = 3 }
            )
            {
                ParentRunId = "run_parent",
            }
        );
        host.Session.HandleEvent(
            new RunFinished("run_child", StopReasons.Stop) { ParentRunId = "run_parent" }
        );
        host.Advance(40);
        Assert.DoesNotContain("subagent running", host.LastFrame, StringComparison.Ordinal);

        // The parent uses the same call id: the maps are keyed by (RunId, CallId).
        host.Session.HandleEvent(new ToolCallStart("run_parent", "call_1", "bash"));
        host.Session.HandleEvent(
            new ToolCallArgs("run_parent", "call_1", """{"command":"parent command"}""")
        );
        host.Session.HandleEvent(new ToolCallEnd("run_parent", "call_1"));
        host.Session.HandleEvent(
            new ToolCallResult("run_parent", "call_1", "parent tool output", IsError: false)
        );
        host.Session.HandleEvent(new TextMessageContent("run_parent", "msg_p", "parent answer"));
        host.Session.HandleEvent(new TextMessageEnd("run_parent", "msg_p"));
        host.Session.HandleEvent(new RunFinished("run_parent", StopReasons.Stop));
        host.Advance(40);

        string transcript = host.ScrollbackText;
        Assert.Contains("parent answer", transcript, StringComparison.Ordinal);
        Assert.Contains("parent tool output", transcript, StringComparison.Ordinal);
        Assert.Contains("parent command", transcript, StringComparison.Ordinal);
        Assert.DoesNotContain("streamed secret", transcript, StringComparison.Ordinal);
        Assert.DoesNotContain("secret child output", transcript, StringComparison.Ordinal);

        // Child usage feeds the footer: 7 + 3 tokens of the cumulative total.
        Assert.Contains("10/", host.LastFrame, StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }
}
