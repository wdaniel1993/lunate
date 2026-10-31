using System.Text;
using System.Text.Json;
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

    private static (AgentHarness Harness, ScriptedChatClient Client) CreateHarness(string root)
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
            new AgentHarnessOptions { WorkingDirectory = root }
        );
        return (harness, client);
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
