using Microsoft.Extensions.AI;

namespace Lunate.Agent.Tests;

internal static class AgentHookPointsTestSupport
{
    public static ScriptedChatClient ScriptedClientWithReadCall() =>
        new ScriptedChatClient()
            .Enqueue(
                new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [
                        new FunctionCallContent(
                            "call-1",
                            "read",
                            new Dictionary<string, object?> { ["path"] = "a.txt" }
                        ),
                    ]
                ),
                new ChatResponseUpdate(ChatRole.Assistant, [])
                {
                    FinishReason = ChatFinishReason.ToolCalls,
                }
            )
            .Enqueue(AgentHookTestSupport.TextUpdate("done", ChatFinishReason.Stop));

    public static ScriptedTool AnnotatedReadTool() =>
        AgentTestSupport.ReadTool("contents", annotations: new ToolAnnotations(ReadOnly: true));

    public static async Task<List<AgentEvent>> Run(
        ScriptedChatClient client,
        ToolRegistry registry,
        AgentHarnessOptions options
    )
    {
        var harness = new AgentHarness(client, registry, options);
        return await AgentTestSupport.Run(harness);
    }

    public static string[] Messages(ScriptedChatClient client) =>
        [
            .. client
                .Requests[0]
                .Messages.SelectMany(m => m.Contents)
                .OfType<TextContent>()
                .Select(t => t.Text),
        ];
}
