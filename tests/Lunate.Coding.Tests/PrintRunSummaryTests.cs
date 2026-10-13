using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>The human print summary ignores child-run events; the JSONL keeps them separately.</summary>
public sealed class PrintRunSummaryTests
{
    [Fact]
    public void Child_text_and_terminal_events_never_reach_the_human_summary()
    {
        var summary = new PrintRunSummary(ApprovalPolicy.Ask);

        summary.Observe(new RunStarted("run_child") { ParentRunId = "run_parent" });
        summary.Observe(new TextMessageStart("run_child", "msg_c") { ParentRunId = "run_parent" });
        summary.Observe(
            new TextMessageContent("run_child", "msg_c", "child answer")
            {
                ParentRunId = "run_parent",
            }
        );
        summary.Observe(new TextMessageEnd("run_child", "msg_c") { ParentRunId = "run_parent" });
        summary.Observe(
            new RunFinished("run_child", StopReasons.TokenLimit) { ParentRunId = "run_parent" }
        );

        Assert.Null(summary.FinalAnswer);
        Assert.Null(summary.StopReason);
        Assert.Null(summary.Error);

        summary.Observe(new TextMessageStart("run_parent", "msg_p"));
        summary.Observe(new TextMessageContent("run_parent", "msg_p", "parent answer"));
        summary.Observe(new TextMessageEnd("run_parent", "msg_p"));
        summary.Observe(new RunFinished("run_parent", StopReasons.Stop));

        Assert.Equal("parent answer", summary.FinalAnswer);
        Assert.Equal(StopReasons.Stop, summary.StopReason);
    }

    [Fact]
    public void Child_usage_events_do_not_produce_stderr_diagnostics()
    {
        var summary = new PrintRunSummary(ApprovalPolicy.Ask);

        string? diagnostic = summary.Observe(
            new ToolCallResult("run_child", "call_1", "child failed", IsError: true)
            {
                ParentRunId = "run_parent",
            }
        );

        Assert.Null(diagnostic);
    }
}
