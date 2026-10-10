using Acp.JsonRpc;
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

    [Fact]
    public async Task The_client_access_cannot_probe_a_prefix()
    {
        using var temp = new TempDirectory();
        var contexts = new List<AcpSessionContext>();
        var model = new AcpScriptedChatClient();
        await using AcpRuntime runtime = AcpTestSupport.Start(context =>
        {
            contexts.Add(context);
            return Harness(model, context);
        });
        await runtime.Client.InitializeAsync(AcpTestSupport.InitializeRequestWithFileSystem, Ct);
        await runtime.Client.NewSessionAsync(
            new NewSessionRequest { Cwd = temp.Root, McpServers = [] },
            Ct
        );

        Assert.Null(Assert.Single(contexts).FileAccess!.ReadPrefix("buffer.txt", 8192));
    }

    [Fact]
    public async Task A_client_buffer_with_a_NUL_is_refused_as_binary()
    {
        using var temp = new TempDirectory();

        string result = await ReadThroughClientAsync(
            temp,
            ReadScript("buffer.txt"),
            runtime => runtime.State.FileReadHandler = _ => "a\0b"
        );

        Assert.Equal("buffer.txt is a binary file (3 bytes); read handles text files", result);
    }

    [Fact]
    public async Task A_client_missing_file_error_surfaces_as_not_found()
    {
        using var temp = new TempDirectory();

        string result = await ReadThroughClientAsync(temp, ReadScript("missing.txt"));

        Assert.Equal("file not found: missing.txt", result);
    }

    [Fact]
    public async Task A_client_read_failure_surfaces_as_an_io_error()
    {
        using var temp = new TempDirectory();

        string result = await ReadThroughClientAsync(
            temp,
            ReadScript("missing.txt"),
            runtime =>
                runtime.State.FileReadError = RequestErrorException.InternalError(
                    additionalMessage: "boom"
                )
        );

        Assert.StartsWith("could not be read: ", result, StringComparison.Ordinal);
        Assert.Contains("boom", result, StringComparison.Ordinal);
    }

    private static AgentHarness Harness(IChatClient client, AcpSessionContext context)
    {
        var workspace = new Workspace(context.Cwd);
        var registry = new ToolRegistry();
        registry.Add(new ReadTool(workspace, context.FileAccess));
        registry.Add(new WriteTool(workspace, files: context.FileAccess));
        return new AgentHarness(client, registry, new AgentHarnessOptions { MaxRetries = 0 });
    }

    private static AcpScriptedChatClient ReadScript(string path) =>
        new AcpScriptedChatClient()
            .Enqueue(
                AcpScripts.Call(
                    "call-1",
                    "read",
                    new Dictionary<string, object?> { ["path"] = path }
                ),
                AcpScripts.ToolCalls()
            )
            .Enqueue(AcpScripts.Text("done"), AcpScripts.Stop());

    private static async Task<string> ReadThroughClientAsync(
        TempDirectory temp,
        AcpScriptedChatClient model,
        Action<AcpRuntime>? configure = null
    )
    {
        await using AcpRuntime runtime = AcpTestSupport.Start(context => Harness(model, context));
        await runtime.Client.InitializeAsync(AcpTestSupport.InitializeRequestWithFileSystem, Ct);
        configure?.Invoke(runtime);
        NewSessionResponse session = await runtime.Client.NewSessionAsync(
            new NewSessionRequest { Cwd = temp.Root, McpServers = [] },
            Ct
        );
        PromptResponse response = await runtime.Client.PromptAsync(
            new PromptRequest
            {
                SessionId = session.SessionId,
                Prompt = [new TextContent { Text = "read it" }],
            },
            Ct
        );
        Assert.Equal(StopReason.EndTurn, response.StopReason);
        return Assert.Single(ReadResults(model));
    }

    private static IEnumerable<string> ReadResults(AcpScriptedChatClient model) =>
        model
            .Requests[^1]
            .Where(message => message.Role == ChatRole.Tool)
            .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
            .Select(result => result.Result?.ToString() ?? string.Empty);
}
