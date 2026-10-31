using System.Text;
using System.Text.Json;
using Lunate.Ai;
using Lunate.Coding;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class ContextProjectionBatchTests
{
    [Fact]
    public async Task A_superseded_read_keeps_its_request_prefix_until_the_batch_turn()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("small.txt"), "before\n");
        (AgentHarness harness, ScriptedChatClient client) = CreateHarness(temp.Root);

        CancellationToken ct = TestContext.Current.CancellationToken;
        await harness.RunAsync("Read the file", ct).ToListAsync(ct);

        Assert.Equal(
            RequestPrefixThroughResult(client.Requests[1], "read-old"),
            RequestPrefixThroughResult(client.Requests[2], "read-old")
        );
    }

    [Fact]
    public async Task A_superseded_read_is_stubbed_at_the_batch_turn()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("small.txt"), "before\n");
        (AgentHarness harness, ScriptedChatClient client) = CreateHarness(temp.Root);

        CancellationToken ct = TestContext.Current.CancellationToken;
        await harness.RunAsync("Read the file", ct).ToListAsync(ct);
        await harness.RunAsync("Continue", ct).ToListAsync(ct);

        FunctionResultContent oldRead = client
            .Requests[3]
            .Messages.SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Single(result => result.CallId == "read-old");
        Assert.Contains("[stale:", oldRead.Result!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_configured_batch_interval_controls_projection_cadence()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("small.txt"), "before\n");
        (AgentHarness harness, ScriptedChatClient client) = CreateHarness(temp.Root, batchTurns: 3);

        CancellationToken ct = TestContext.Current.CancellationToken;
        await harness.RunAsync("Read the file", ct).ToListAsync(ct);

        FunctionResultContent oldRead = client
            .Requests[2]
            .Messages.SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Single(result => result.CallId == "read-old");
        Assert.Contains("[stale:", oldRead.Result!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_compaction_applies_a_pending_projection_batch()
    {
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("session.jsonl"), temp.Root);
        session.AppendMessage(new ChatMessage(ChatRole.User, new string('x', 5_000)));
        session.AppendMessage(new ChatMessage(ChatRole.Assistant, "older answer"));
        AppendReadTurn(session, "Read first version", "read-old", "old contents");
        AppendReadTurn(session, "Read second version", "read-new", "new contents");
        var chatClient = new ScriptedChatClient().Enqueue(
            LoopScripts.Text("Continue"),
            LoopScripts.Stop()
        );
        var compactionClient = new ScriptedChatClient().Enqueue(
            LoopScripts.Text("Summary"),
            LoopScripts.Stop()
        );
        var harness = new AgentHarness(
            chatClient,
            new ToolRegistry(),
            new AgentHarnessOptions
            {
                Session = session,
                WorkingDirectory = temp.Root,
                ModelId = TestCatalog.ModelId,
                ModelCatalog = TestCatalog.WithWindow(1_000),
                CompactionKeepTurns = 3,
                ProjectionBatchTurns = 10,
                UsageModels = new Dictionary<string, UsageModelClient>(StringComparer.Ordinal)
                {
                    ["compaction"] = new(
                        compactionClient,
                        "compact-profile",
                        "openai",
                        new ModelInfo("compact-model", "openai", null, 1_000, SupportsTools: true)
                    ),
                },
            }
        );

        CancellationToken ct = TestContext.Current.CancellationToken;
        List<AgentEvent> events = await harness.RunAsync("Continue", ct).ToListAsync(ct);

        Assert.Single(events.OfType<CompactionApplied>());
        FunctionResultContent oldRead = chatClient
            .Requests.Single()
            .Messages.SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Single(result => result.CallId == "read-old");
        Assert.Contains("[stale:", oldRead.Result!.ToString(), StringComparison.Ordinal);
    }

    private static (AgentHarness Harness, ScriptedChatClient Client) CreateHarness(
        string root,
        int batchTurns = 4
    )
    {
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("read-old", "read", LoopScripts.Args(("path", "small.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("read-new", "read", LoopScripts.Args(("path", "small.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Read"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("Continue"), LoopScripts.Stop());
        var harness = new AgentHarness(
            new ContextAccountingChatClient(client),
            Registry(new ReadTool(new Workspace(root))),
            new AgentHarnessOptions { WorkingDirectory = root, ProjectionBatchTurns = batchTurns }
        );
        return (harness, client);
    }

    private static void AppendReadTurn(Session session, string user, string callId, string output)
    {
        session.AppendMessage(new ChatMessage(ChatRole.User, user));
        session.AppendMessage(
            new ChatMessage(
                ChatRole.Assistant,
                [
                    new FunctionCallContent(
                        callId,
                        "read",
                        new Dictionary<string, object?> { ["path"] = "small.txt" }
                    ),
                ]
            )
        );
        session.AppendMessage(
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, output)])
        );
    }

    private static byte[] RequestPrefixThroughResult(ScriptedRequest request, string callId)
    {
        int resultIndex = request
            .Messages.Select((message, index) => (message, index))
            .Single(item =>
                item.message.Contents.OfType<FunctionResultContent>()
                    .Any(result => result.CallId == callId)
            )
            .index;
        string prefix = string.Join(
            "\n",
            request
                .Messages.Take(resultIndex + 1)
                .Select(message =>
                    $"{message.Role}|{string.Join('|', message.Contents.Select(ContentSnapshot))}"
                )
        );
        return Encoding.UTF8.GetBytes(prefix);
    }

    private static string ContentSnapshot(AIContent content) =>
        content switch
        {
            TextContent text => $"text:{text.Text}",
            FunctionCallContent call =>
                $"call:{call.CallId}:{call.Name}:{JsonSerializer.Serialize(call.Arguments)}",
            FunctionResultContent result => $"result:{result.CallId}:{result.Result}",
            _ => $"{content.GetType().Name}:{content}",
        };
}
