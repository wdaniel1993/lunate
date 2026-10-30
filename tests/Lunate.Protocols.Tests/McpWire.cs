using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Protocols.Tests;

internal sealed class DeclaredTool(
    string name,
    string description,
    string schema,
    Func<JsonElement, ToolResult> run,
    bool readOnly = false
) : ITool
{
    private readonly JsonElement _schema = JsonDocument.Parse(schema).RootElement.Clone();

    public string Name => name;

    public string Description => description;

    public JsonElement ParametersSchema => _schema;

    public ToolAnnotations Annotations => new(ReadOnly: readOnly);

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct) =>
        Task.FromResult(run(args));
}

/// <summary>A raw NDJSON client on the client end of two pipe pairs.</summary>
internal sealed class McpWire : IAsyncDisposable
{
    private readonly Pipe _toServer = new();
    private readonly Pipe _toClient = new();
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly Task _serverTask;

    private McpWire(
        IReadOnlyList<ITool> tools,
        string root,
        IReadOnlyList<string>? roots,
        Func<IReadOnlyList<string>?, McpWorkspaceToolSet>? resolveToolSet
    )
    {
        _reader = new StreamReader(_toClient.Reader.AsStream(), Encoding.UTF8);
        _writer = new StreamWriter(_toServer.Writer.AsStream(), new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
        _serverTask = Task.Run(() =>
            McpToolServer.RunAsync(
                tools,
                "test-server",
                "1.0.0",
                "test instructions",
                root,
                _toServer.Reader.AsStream(),
                _toClient.Writer.AsStream(),
                CancellationToken.None,
                resolveToolSet
            )
        );
        Roots = roots;
    }

    public IReadOnlyList<string>? Roots { get; set; }

    public List<JsonElement> Notifications { get; } = [];

    public static McpWire Start(
        IReadOnlyList<ITool> tools,
        string root,
        IReadOnlyList<string>? roots = null,
        Func<IReadOnlyList<string>?, McpWorkspaceToolSet>? resolveToolSet = null
    ) => new(tools, root, roots, resolveToolSet);

    public async Task<JsonElement> RequestAsync(int id, string method, object parameters)
    {
        await _writer
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
            JsonElement frame = await ReadFrameAsync();
            Assert.Equal("2.0", frame.GetProperty("jsonrpc").GetString());
            if (await RespondToRootsListAsync(frame))
            {
                continue;
            }

            if (!frame.TryGetProperty("id", out JsonElement frameId))
            {
                Notifications.Add(frame);
                continue;
            }

            if (frameId.GetInt32() == id)
            {
                Assert.True(
                    frame.TryGetProperty("result", out _) || frame.TryGetProperty("error", out _),
                    $"stdout line is not a JSON-RPC response frame: {frame}"
                );
                return frame;
            }
        }
    }

    public async Task SendNotificationAsync(string method) =>
        await _writer.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", method }));

    public async Task<JsonElement> WaitForNotificationAsync(string method)
    {
        while (true)
        {
            JsonElement frame = await ReadFrameAsync();
            if (await RespondToRootsListAsync(frame))
            {
                continue;
            }

            if (!frame.TryGetProperty("id", out _) && frame.TryGetProperty("method", out _))
            {
                Notifications.Add(frame);
                if (frame.GetProperty("method").GetString() == method)
                {
                    return frame;
                }
            }
        }
    }

    private async Task<bool> RespondToRootsListAsync(JsonElement frame)
    {
        if (
            !frame.TryGetProperty("method", out JsonElement method)
            || method.GetString() != "roots/list"
        )
        {
            return false;
        }

        await _writer.WriteLineAsync(
            JsonSerializer.Serialize(
                new
                {
                    jsonrpc = "2.0",
                    id = frame.GetProperty("id"),
                    result = new
                    {
                        roots = Roots
                            ?.Select(uri => new Dictionary<string, string> { ["uri"] = uri })
                            .ToArray()
                            ?? [],
                    },
                }
            )
        );
        return true;
    }

    private async Task<JsonElement> ReadFrameAsync()
    {
        string? line = await _reader
            .ReadLineAsync(TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.NotNull(line);
        using JsonDocument document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        await _toServer.Writer.CompleteAsync();
        await Task.WhenAny(_serverTask, Task.Delay(TimeSpan.FromSeconds(10)))
            .WaitAsync(TestContext.Current.CancellationToken);
        _reader.Dispose();
        await _toClient.Reader.AsStream().DisposeAsync();
    }
}
