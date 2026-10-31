using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class AgentToolSessionContextTests
{
    [Fact]
    public async Task A_resumed_run_stamps_its_session_into_the_tool_context()
    {
        using var temp = new TempDirectory();
        Session original = Session.Create(temp.File("session.jsonl"), temp.Root);
        Session resumed = Session.Load(original.Path);
        var tool = new ScriptedTool("probe", "Probe context.", """{"type":"object"}""");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "probe", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(tool),
            new AgentHarnessOptions
            {
                Session = resumed,
                WorkingDirectory = temp.Root,
                ToolOutputLimit = 123,
                SpillThresholdTokens = 456,
            }
        );

        await Run(harness);

        ToolContext received = tool.ReceivedContext!;
        Assert.Same(resumed, AgentToolContextState.SessionFor(received));
        Assert.NotNull(AgentToolContextState.ReadsFor(received));
        Assert.Equal(123, received.ToolOutputLimit);
        Assert.Equal(456, received.SpillThresholdTokens);
    }

    [Fact]
    public void A_hand_built_context_has_no_read_ledger()
    {
        var context = new ToolContext("/work", new AgentEventChannel());

        Assert.Null(AgentToolContextState.ReadsFor(context));
    }
}
