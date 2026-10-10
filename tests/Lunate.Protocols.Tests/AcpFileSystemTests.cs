using Acp.Schema;
using Lunate.Agent;
using Lunate.Coding;
using Lunate.Protocols.Acp;
using ChatRole = Microsoft.Extensions.AI.ChatRole;
using FunctionResultContent = Microsoft.Extensions.AI.FunctionResultContent;
using IChatClient = Microsoft.Extensions.AI.IChatClient;

namespace Lunate.Protocols.Tests;

public sealed class AcpFileSystemTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Editor_buffers_are_respected_and_writes_go_to_the_client()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("buffer.txt"), "disk content\n");
        File.WriteAllText(temp.File("w.txt"), "old local\n");
        string root = new Workspace(temp.Root).WorktreeRoot;
        var contexts = new List<AcpSessionContext>();
        var model = new AcpScriptedChatClient()
            .Enqueue(
                AcpScripts.Call(
                    "call-1",
                    "read",
                    new Dictionary<string, object?> { ["path"] = "buffer.txt" }
                ),
                AcpScripts.Call(
                    "call-2",
                    "write",
                    new Dictionary<string, object?>
                    {
                        ["path"] = "w.txt",
                        ["content"] = "client content\n",
                    }
                ),
                AcpScripts.ToolCalls()
            )
            .Enqueue(AcpScripts.Text("done"), AcpScripts.Stop());
        await using AcpRuntime runtime = AcpTestSupport.Start(context =>
        {
            contexts.Add(context);
            return Harness(model, context);
        });
        await runtime.Client.InitializeAsync(AcpTestSupport.InitializeRequestWithFileSystem, Ct);
        runtime.State.FileReadHandler = request =>
            request.Path.EndsWith("buffer.txt", StringComparison.Ordinal)
                ? "buffer content\n"
                : "old local\n";
        NewSessionResponse session = await runtime.Client.NewSessionAsync(
            new NewSessionRequest { Cwd = temp.Root, McpServers = [] },
            Ct
        );

        PromptResponse response = await runtime.Client.PromptAsync(
            new PromptRequest
            {
                SessionId = session.SessionId,
                Prompt = [new TextContent { Text = "edit the buffer" }],
            },
            Ct
        );

        Assert.Equal(StopReason.EndTurn, response.StopReason);
        AcpSessionContext context = Assert.Single(contexts);
        Assert.NotNull(context.FileAccess);
        Assert.Contains(
            ReadResults(model),
            result => result.Contains("buffer content", StringComparison.Ordinal)
        );
        Assert.Equal("disk content\n", File.ReadAllText(temp.File("buffer.txt")));
        Assert.Contains(
            runtime.State.FileReads,
            read => read.Path == Path.Combine(root, "buffer.txt")
        );
        WriteTextFileRequest write = Assert.Single(runtime.State.FileWrites);
        Assert.Equal(Path.Combine(root, "w.txt"), write.Path);
        Assert.Equal("client content\n", write.Content);
        Assert.Equal("old local\n", File.ReadAllText(temp.File("w.txt")));
    }

    [Fact]
    public async Task Without_the_capability_the_local_disk_is_used()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("buffer.txt"), "disk content\n");
        var contexts = new List<AcpSessionContext>();
        var model = new AcpScriptedChatClient()
            .Enqueue(
                AcpScripts.Call(
                    "call-1",
                    "read",
                    new Dictionary<string, object?> { ["path"] = "buffer.txt" }
                ),
                AcpScripts.Call(
                    "call-2",
                    "write",
                    new Dictionary<string, object?>
                    {
                        ["path"] = "new.txt",
                        ["content"] = "local write\n",
                    }
                ),
                AcpScripts.ToolCalls()
            )
            .Enqueue(AcpScripts.Text("done"), AcpScripts.Stop());
        await using AcpRuntime runtime = AcpTestSupport.Start(context =>
        {
            contexts.Add(context);
            return Harness(model, context);
        });
        await runtime.Client.InitializeAsync(AcpTestSupport.InitializeRequest, Ct);
        NewSessionResponse session = await runtime.Client.NewSessionAsync(
            new NewSessionRequest { Cwd = temp.Root, McpServers = [] },
            Ct
        );

        PromptResponse response = await runtime.Client.PromptAsync(
            new PromptRequest
            {
                SessionId = session.SessionId,
                Prompt = [new TextContent { Text = "use the disk" }],
            },
            Ct
        );

        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Null(Assert.Single(contexts).FileAccess);
        Assert.Empty(runtime.State.FileReads);
        Assert.Empty(runtime.State.FileWrites);
        Assert.Contains(
            ReadResults(model),
            result => result.Contains("disk content", StringComparison.Ordinal)
        );
        Assert.Equal("local write\n", File.ReadAllText(temp.File("new.txt")));
    }

    private static AgentHarness Harness(IChatClient client, AcpSessionContext context)
    {
        var workspace = new Workspace(context.Cwd);
        var registry = new ToolRegistry();
        registry.Add(new ReadTool(workspace, context.FileAccess));
        registry.Add(new WriteTool(workspace, files: context.FileAccess));
        return new AgentHarness(client, registry, new AgentHarnessOptions { MaxRetries = 0 });
    }

    private static IEnumerable<string> ReadResults(AcpScriptedChatClient model) =>
        model
            .Requests[^1]
            .Where(message => message.Role == ChatRole.Tool)
            .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
            .Select(result => result.Result?.ToString() ?? string.Empty);
}
