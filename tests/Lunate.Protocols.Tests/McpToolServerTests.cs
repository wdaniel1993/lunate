using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Lunate.Agent;
using ModelContextProtocol.Protocol;

namespace Lunate.Protocols.Tests;

/// <summary>
/// The stdio server over <see cref="ITool"/>s: initialize and tools/list answer from the tools' own
/// declarations, tools/call runs the tool, and every stdout line is a JSON-RPC frame.
/// </summary>
public sealed class McpToolServerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Initialize_and_tools_list_answer_from_the_tool_declarations()
    {
        using var temp = new TempDirectory();
        var echo = new DeclaredTool(
            "echo",
            "Echo the input text back.",
            """
            {
              "type": "object",
              "properties": { "text": { "type": "string" } },
              "required": ["text"]
            }
            """,
            args => new ToolResult(args.GetProperty("text").GetString() ?? string.Empty, false),
            readOnly: true
        );
        await using var wire = McpWire.Start([echo], temp.Root);

        JsonElement initialize = await wire.RequestAsync(
            1,
            "initialize",
            new
            {
                protocolVersion = McpToolServer.ProtocolVersion,
                clientInfo = new { name = "test-client", version = "0.0.1" },
                capabilities = new { },
            }
        );

        JsonElement initResult = initialize.GetProperty("result");
        Assert.Equal(
            "test-server",
            initResult.GetProperty("serverInfo").GetProperty("name").GetString()
        );
        Assert.True(initResult.GetProperty("capabilities").TryGetProperty("tools", out _));

        JsonElement list = await wire.RequestAsync(2, "tools/list", new { });
        JsonElement[] tools =
        [
            .. list.GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Select(tool => tool.Clone()),
        ];

        JsonElement listed = Assert.Single(tools);
        Assert.Equal("echo", listed.GetProperty("name").GetString());
        Assert.Equal("Echo the input text back.", listed.GetProperty("description").GetString());
        Assert.True(
            listed
                .GetProperty("inputSchema")
                .GetProperty("properties")
                .TryGetProperty("text", out _)
        );
        Assert.True(listed.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
    }

    [Fact]
    public async Task A_tool_call_runs_the_tool_and_maps_its_result()
    {
        using var temp = new TempDirectory();
        JsonElement? seen = null;
        var echo = new DeclaredTool(
            "echo",
            "Echo the input text back.",
            """{"type":"object","properties":{"text":{"type":"string"}}}""",
            args =>
            {
                seen = args.Clone();
                return new ToolResult($"echo: {args.GetProperty("text").GetString()}", false);
            },
            readOnly: true
        );
        await using var wire = McpWire.Start([echo], temp.Root);

        JsonElement response = await wire.RequestAsync(
            1,
            "tools/call",
            new { name = "echo", arguments = new { text = "hi" } }
        );

        JsonElement result = response.GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var isError) && isError.GetBoolean());
        Assert.Equal("echo: hi", result.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("hi", seen?.GetProperty("text").GetString());
    }

    [Fact]
    public async Task A_failing_tool_call_comes_back_as_an_error_result()
    {
        using var temp = new TempDirectory();
        var failing = new DeclaredTool(
            "fail",
            "Always fails.",
            """{"type":"object","properties":{}}""",
            _ => new ToolResult("the tool failed on purpose", true),
            readOnly: true
        );
        await using var wire = McpWire.Start([failing], temp.Root);

        JsonElement response = await wire.RequestAsync(
            1,
            "tools/call",
            new { name = "fail", arguments = new { } }
        );

        JsonElement result = response.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Equal(
            "the tool failed on purpose",
            result.GetProperty("content")[0].GetProperty("text").GetString()
        );
    }

    [Fact]
    public async Task An_unknown_method_is_a_json_rpc_error_not_a_stdout_contamination()
    {
        using var temp = new TempDirectory();
        await using var wire = McpWire.Start([], temp.Root);

        JsonElement response = await wire.RequestAsync(1, "lunate/bogus", new { });

        Assert.Equal(-32601, response.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task A_tool_that_could_change_the_workspace_is_refused()
    {
        var write = new DeclaredTool(
            "write",
            "Writes files.",
            """{"type":"object","properties":{}}""",
            _ => new ToolResult("written", false)
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                McpToolServer.RunAsync(
                    [write],
                    "test-server",
                    "1.0.0",
                    "test instructions",
                    Path.GetTempPath(),
                    Stream.Null,
                    Stream.Null,
                    Ct
                )
        );

        Assert.Contains(
            "'write' does not declare readOnly",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    private sealed class DeclaredTool(
        string name,
        string description,
        string schema,
        Func<JsonElement, ToolResult> run,
        bool readOnly = false
    ) : ITool
    {
        private readonly JsonElement schema = JsonDocument.Parse(schema).RootElement.Clone();

        public string Name => name;

        public string Description => description;

        public JsonElement ParametersSchema => schema;

        public Lunate.Agent.ToolAnnotations Annotations => new(ReadOnly: readOnly);

        public Task<ToolResult> ExecuteAsync(
            JsonElement args,
            ToolContext ctx,
            CancellationToken ct
        ) => Task.FromResult(run(args));
    }
}

/// <summary>A raw NDJSON client on the client end of two pipe pairs.</summary>
internal sealed class McpWire : IAsyncDisposable
{
    private readonly Pipe toServer = new();
    private readonly Pipe toClient = new();
    private readonly StreamReader reader;
    private readonly StreamWriter writer;
    private readonly Task serverTask;

    private McpWire(IReadOnlyList<ITool> tools, string root)
    {
        reader = new StreamReader(toClient.Reader.AsStream(), Encoding.UTF8);
        writer = new StreamWriter(toServer.Writer.AsStream(), new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
        serverTask = Task.Run(() =>
            McpToolServer.RunAsync(
                tools,
                "test-server",
                "1.0.0",
                "test instructions",
                root,
                toServer.Reader.AsStream(),
                toClient.Writer.AsStream(),
                CancellationToken.None
            )
        );
    }

    public static McpWire Start(IReadOnlyList<ITool> tools, string root) => new(tools, root);

    public async Task<JsonElement> RequestAsync(int id, string method, object parameters)
    {
        await writer
            .WriteLineAsync(
                JsonSerializer.Serialize(
                    new
                    {
                        jsonrpc = "2.0",
                        id,
                        method,
                        @params = parameters,
                    }
                )
            )
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        while (true)
        {
            string line = await ReadLineAsync();
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement frame = document.RootElement.Clone();
            Assert.Equal("2.0", frame.GetProperty("jsonrpc").GetString());
            if (frame.TryGetProperty("id", out JsonElement frameId) && frameId.GetInt32() == id)
            {
                Assert.True(
                    frame.TryGetProperty("result", out _) || frame.TryGetProperty("error", out _),
                    $"stdout line is not a JSON-RPC response frame: {line}"
                );
                return frame;
            }
        }
    }

    private async Task<string> ReadLineAsync()
    {
        string? line = await reader
            .ReadLineAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.NotNull(line);
        return line;
    }

    public async ValueTask DisposeAsync()
    {
        await writer.DisposeAsync();
        await toServer.Writer.CompleteAsync();
        await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(10)))
            .WaitAsync(TestContext.Current.CancellationToken);
        reader.Dispose();
        await toClient.Reader.AsStream().DisposeAsync();
    }
}
