using Acp.Schema;

namespace Lunate.Protocols.Tests;

/// <summary>A run's reasoning streams to the ACP client as agent_thought_chunk updates.</summary>
public sealed class AcpReasoningTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reasoning_streams_as_thought_chunks_in_event_order()
    {
        using var temp = new TempDirectory();
        var model = new AcpScriptedChatClient().Enqueue(
            AcpScripts.Reasoning("Think "),
            AcpScripts.Reasoning("one"),
            AcpScripts.Text("Answer"),
            AcpScripts.Stop()
        );
        await using AcpRuntime runtime = AcpTestSupport.Start(_ => AcpTestSupport.Harness(model));

        await runtime.Client.InitializeAsync(AcpTestSupport.InitializeRequest, Ct);
        NewSessionResponse session = await runtime.Client.NewSessionAsync(
            new NewSessionRequest { Cwd = temp.Root, McpServers = [] },
            Ct
        );
        PromptResponse response = await runtime.Client.PromptAsync(
            new PromptRequest
            {
                SessionId = session.SessionId,
                Prompt = [new TextContent { Text = "go" }],
            },
            Ct
        );

        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Collection(
            runtime.State.Updates,
            update =>
                Assert.Equal(
                    """{"sessionUpdate":"agent_thought_chunk","content":{"type":"text","text":"Think "}}""",
                    AcpTestSupport.Wire(update.Update)
                ),
            update =>
                Assert.Equal(
                    """{"sessionUpdate":"agent_thought_chunk","content":{"type":"text","text":"one"}}""",
                    AcpTestSupport.Wire(update.Update)
                ),
            update =>
                Assert.Equal(
                    """{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"Answer"}}""",
                    AcpTestSupport.Wire(update.Update)
                )
        );
    }
}
