using Lunate.Agent;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The session's in-memory transcript store: the viewer's view over data the session already
/// holds. User and assistant text, thinking spans and full tool blocks (output and diff included)
/// append in commit order; child-run events stay out entirely, and nothing new is persisted.
/// </summary>
public sealed class InteractiveSessionTranscriptTests
{
    [Fact]
    public async Task Committed_items_keep_the_commit_order_and_content()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Session.HandleEvent(new RunStarted("run"));
        host.Session.HandleEvent(new ReasoningStart("run", "m0"));
        host.Session.HandleEvent(new ReasoningMessageContent("run", "m0", "weighing options"));
        host.Session.HandleEvent(new ReasoningEnd("run", "m0"));
        host.Session.HandleEvent(new TextMessageContent("run", "m1", "done"));
        host.Session.HandleEvent(new TextMessageEnd("run", "m1"));
        host.Session.HandleEvent(new ToolCallStart("run", "call_1", "read"));
        host.Session.HandleEvent(new ToolCallArgs("run", "call_1", """{"path":"a.txt"}"""));
        host.Session.HandleEvent(new ToolCallResult("run", "call_1", "content", false));
        host.Advance(40);

        Assert.Collection(
            host.Session.Transcript,
            entry =>
            {
                var thinking = Assert.IsType<TranscriptThinkingEntry>(entry);
                Assert.Equal("weighing options", thinking.Commit.Text);
                Assert.Equal(0, thinking.Commit.ElapsedSeconds);
            },
            entry => Assert.Equal("done", Assert.IsType<TranscriptAssistantEntry>(entry).Markdown),
            entry => Assert.Equal("read", Assert.IsType<TranscriptToolEntry>(entry).Block.ToolName)
        );

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_tool_entry_carries_the_full_output_and_diff()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        string output = string.Join(
            '\n',
            Enumerable.Range(1, 30).Select(n => FormattableString.Invariant($"line {n}"))
        );
        const string diff = "--- a/src/a.cs\n+++ b/src/a.cs\n@@ -4,2 +4,2 @@\n-old\n+new";

        host.Session.HandleEvent(new RunStarted("run"));
        host.Session.HandleEvent(new ToolCallStart("run", "call_1", "edit"));
        host.Session.HandleEvent(new ToolCallArgs("run", "call_1", """{"path":"src/a.cs"}"""));
        host.Session.HandleEvent(
            new ToolCallResult(
                "run",
                "call_1",
                output,
                false,
                new EditDetails("src/a.cs", 4, 5, "exact", diff)
            )
        );
        host.Advance(40);

        var tool = Assert.IsType<TranscriptToolEntry>(Assert.Single(host.Session.Transcript));
        Assert.Equal(output, tool.Block.Output);
        Assert.Equal(new ToolDiffInfo("src/a.cs", "exact", diff), tool.Block.Diff);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task User_text_enters_the_transcript_at_submission()
    {
        using var host = new InteractiveSessionHost();
        host.Client.Enqueue(Scripts.Text("explained"), Scripts.Stop());
        Task run = host.RunAsync();

        host.Console.SendText("explain");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Session.Transcript.Count > 0);
        await host.WaitUntilAsync(() =>
            host.Session.Transcript.Any(entry => entry is TranscriptAssistantEntry)
        );

        IReadOnlyList<TranscriptEntry> transcript = host.Session.Transcript;
        Assert.Equal("explain", Assert.IsType<TranscriptUserEntry>(transcript[0]).Text);
        Assert.Equal("explained", Assert.IsType<TranscriptAssistantEntry>(transcript[1]).Markdown);

        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task Child_run_events_stay_out_of_the_transcript()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Session.HandleEvent(new RunStarted("run_parent"));
        host.Session.HandleEvent(new RunStarted("run_child") { ParentRunId = "run_parent" });
        host.Session.HandleEvent(
            new ReasoningStart("run_child", "msg_c") { ParentRunId = "run_parent" }
        );
        host.Session.HandleEvent(
            new ReasoningMessageContent("run_child", "msg_c", "secret child reasoning")
            {
                ParentRunId = "run_parent",
            }
        );
        host.Session.HandleEvent(
            new ReasoningEnd("run_child", "msg_c") { ParentRunId = "run_parent" }
        );
        host.Session.HandleEvent(
            new TextMessageContent("run_child", "msg_c2", "secret child text")
            {
                ParentRunId = "run_parent",
            }
        );
        host.Session.HandleEvent(
            new TextMessageEnd("run_child", "msg_c2") { ParentRunId = "run_parent" }
        );
        host.Session.HandleEvent(
            new ToolCallStart("run_child", "call_c", "read") { ParentRunId = "run_parent" }
        );
        host.Session.HandleEvent(
            new ToolCallArgs("run_child", "call_c", """{"path":"secret.txt"}""")
            {
                ParentRunId = "run_parent",
            }
        );
        host.Session.HandleEvent(
            new ToolCallResult("run_child", "call_c", "secret child output", false)
            {
                ParentRunId = "run_parent",
            }
        );
        host.Session.HandleEvent(
            new RunFinished("run_child", StopReasons.Stop) { ParentRunId = "run_parent" }
        );
        host.Session.HandleEvent(new TextMessageContent("run_parent", "msg_p", "parent text"));
        host.Session.HandleEvent(new TextMessageEnd("run_parent", "msg_p"));
        host.Advance(40);

        var parent = Assert.IsType<TranscriptAssistantEntry>(
            Assert.Single(host.Session.Transcript)
        );
        Assert.Equal("parent text", parent.Markdown);

        host.Console.Complete();
        await run;
    }
}
