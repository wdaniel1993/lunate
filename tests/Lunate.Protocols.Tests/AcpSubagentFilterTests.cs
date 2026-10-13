using System.Text.Json;
using Acp.Schema;
using Lunate.Agent;

namespace Lunate.Protocols.Tests;

/// <summary>Child-run (subagent) events are filtered out of ACP session/update, never passed raw.</summary>
public sealed class AcpSubagentFilterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Child_events_never_reach_the_client_raw()
    {
        using var temp = new TempDirectory();
        var model = new AcpScriptedChatClient()
            .Enqueue(
                AcpScripts.Call("call-1", "spawn", Args(("prompt", "child"))),
                AcpScripts.ToolCalls()
            )
            .Enqueue(
                AcpScripts.Call("call-1", "echo", Args(("message", "hi"))),
                AcpScripts.ToolCalls()
            )
            .Enqueue(AcpScripts.Text("streamed secret"), AcpScripts.Stop())
            .Enqueue(AcpScripts.Text("parent text"), AcpScripts.Stop());
        await using AcpRuntime runtime = AcpTestSupport.Start(_ =>
            AcpTestSupport.Harness(model, new SpawnTool(), new EchoTool())
        );

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
        IReadOnlyList<SessionUpdate> updates =
        [
            .. runtime.State.Updates.Select(notification => notification.Update),
        ];
        Assert.DoesNotContain(
            updates,
            update =>
                update is AgentMessageChunk { Content: TextContent text }
                && text.Text == "streamed secret"
        );
        Assert.Contains(
            updates,
            update =>
                update is AgentMessageChunk { Content: TextContent text }
                && text.Text == "parent text"
        );
        Assert.Contains(updates, update => update is ToolCallStartUpdate { Title: "spawn" });
        // The child's own tool call (same call id) never became a second tool call on the wire.
        Assert.DoesNotContain(updates, update => update is ToolCallStartUpdate { Title: "echo" });
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] entries)
    {
        var arguments = new Dictionary<string, object?>();
        foreach ((string key, object? value) in entries)
        {
            arguments[key] = value;
        }

        return arguments;
    }

    private sealed class SpawnTool : ITool
    {
        public string Name => "spawn";

        public string Description => "Spawns a subagent.";

        public JsonElement ParametersSchema { get; } =
            JsonSerializer.SerializeToElement(
                new
                {
                    type = "object",
                    properties = new { prompt = new { type = "string" } },
                    required = new[] { "prompt" },
                }
            );

        public ToolRisk Risk => ToolRisk.ReadOnly;

        public async Task<ToolResult> ExecuteAsync(
            JsonElement args,
            ToolContext ctx,
            CancellationToken ct
        )
        {
            SubagentResult result = await ctx.Subagents!.RunAsync(
                new SubagentRequest(args.GetProperty("prompt").GetString()!),
                ct
            );
            return new ToolResult(result.Output, result.IsError);
        }
    }
}
