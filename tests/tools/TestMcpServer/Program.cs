using System.Globalization;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

// Tiny stdio MCP server for Lunate's protocol tests. stdio carries protocol only:
// markers go to the file named by LUNATE_TEST_MCP_MARKER, everything else to stderr.

var markerPath = Environment.GetEnvironmentVariable("LUNATE_TEST_MCP_MARKER");
var markerLock = new object();

// LUNATE_TEST_MCP_SLOW_LIST_MS delays every tools/list after the first by the given milliseconds,
// so a client's bounded liveness probe times out against an otherwise healthy server.
_ = int.TryParse(
    Environment.GetEnvironmentVariable("LUNATE_TEST_MCP_SLOW_LIST_MS"),
    NumberStyles.Integer,
    CultureInfo.InvariantCulture,
    out var slowListMs
);

// LUNATE_TEST_MCP_SLOW_START_MS delays the initialize handshake by the given milliseconds, so a
// client meets a live but slow server. LUNATE_TEST_MCP_START_HEARTBEAT_MS additionally writes a
// stderr line at that interval while the delay runs: stderr is the only child output a client can
// observe before the handshake completes. Zero or absent heartbeat means silence.
_ = int.TryParse(
    Environment.GetEnvironmentVariable("LUNATE_TEST_MCP_SLOW_START_MS"),
    NumberStyles.Integer,
    CultureInfo.InvariantCulture,
    out var slowStartMs
);
_ = int.TryParse(
    Environment.GetEnvironmentVariable("LUNATE_TEST_MCP_START_HEARTBEAT_MS"),
    NumberStyles.Integer,
    CultureInfo.InvariantCulture,
    out var startHeartbeatMs
);

void Mark(string line)
{
    if (string.IsNullOrEmpty(markerPath))
    {
        return;
    }

    lock (markerLock)
    {
        // The tests poll this file while the server runs, so an append can collide with a reader
        // that holds the file with a narrower share mode (a sharing violation on Windows). Retry a
        // bounded number of times, and if it still fails say so on stderr instead of losing the
        // line silently: a lost marker is indistinguishable from a server that never got there.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.AppendAllText(markerPath, line + Environment.NewLine);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(20);
            }
            catch (IOException exception)
            {
                Console.Error.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"test-mcp-server: marker line '{line}' was not written: {exception.Message}"
                    )
                );
                return;
            }
        }
    }
}

Mark(string.Create(CultureInfo.InvariantCulture, $"started pid={Environment.ProcessId}"));

var tools = new McpServerPrimitiveCollection<McpServerTool>();

// Every tool is added BEFORE McpServer.Create: the server wires the collection's Changed
// events to tools/list_changed notifications during Create, so adds that happen later but
// still pre-session can queue stray notifications that land once the client session starts.
tools.Add(
    McpServerTool.Create(
        (Func<string, string>)Echo,
        new McpServerToolCreateOptions
        {
            Name = "echo",
            Description = "Echoes the input text back.",
            ReadOnly = true,
        }
    )
);
tools.Add(
    McpServerTool.Create(
        (Func<int, int, AddResult>)Add,
        new McpServerToolCreateOptions
        {
            Name = "add",
            Description = "Adds two integers and returns the sum.",
            UseStructuredContent = true,
        }
    )
);
tools.Add(
    McpServerTool.Create(
        (Func<int, CancellationToken, Task<string>>)SlowAsync,
        new McpServerToolCreateOptions
        {
            Name = "slow",
            Description = "Sleeps for the given number of seconds, honoring cancellation.",
        }
    )
);
tools.Add(
    McpServerTool.Create(
        (Func<string>)Boom,
        new McpServerToolCreateOptions { Name = "boom", Description = "Always fails." }
    )
);
tools.Add(
    McpServerTool.Create(
        (Func<RequestContext<CallToolRequestParams>, CancellationToken, Task<string>>)ElicitAsync,
        new McpServerToolCreateOptions
        {
            Name = "elicit",
            Description = "Requests a form elicitation and reports the response shape.",
        }
    )
);
tools.Add(
    McpServerTool.Create(
        (Func<RequestContext<CallToolRequestParams>, CancellationToken, Task<string>>)
            ElicitFormAsync,
        new McpServerToolCreateOptions
        {
            Name = "elicit_form",
            Description = "Requests a flat form elicitation and reports the response.",
        }
    )
);
tools.Add(
    McpServerTool.Create(
        (Func<RequestContext<CallToolRequestParams>, CancellationToken, Task<string>>)
            ElicitUrlAsync,
        new McpServerToolCreateOptions
        {
            Name = "elicit_url",
            Description = "Requests a URL-mode elicitation and reports the response.",
        }
    )
);
tools.Add(
    McpServerTool.Create(
        (Func<string>)SpawnTool,
        new McpServerToolCreateOptions
        {
            Name = "spawn_tool",
            Description = "Registers late_tool and notifies a list change.",
        }
    )
);

var serverOptions = new McpServerOptions
{
    ServerInfo = new Implementation { Name = "test-mcp-server", Version = "1.0.0" },
    Capabilities = new ServerCapabilities { Tools = new ToolsCapability { ListChanged = true } },
    ToolCollection = tools,
    // Records that the notification reached the server at all, separately from the tool's own
    // `slow_cancelled` line: a failure then names the leg that lost the message — never sent,
    // or sent but not acted on.
    Handlers = new McpServerHandlers
    {
        NotificationHandlers =
        [
            new(
                NotificationMethods.CancelledNotification,
                (_, _) =>
                {
                    Mark("cancel_notice");
                    return default;
                }
            ),
        ],
    },
};
if (slowListMs > 0)
{
    // The handler answers immediately on the first tools/list so a host starts normally; the SDK
    // appends the collection's tools to the empty result the handler returns.
    var listCalls = 0;
    serverOptions.Handlers.ListToolsHandler = async (_, cancellationToken) =>
    {
        if (Interlocked.Increment(ref listCalls) > 1)
        {
            await Task.Delay(slowListMs, cancellationToken);
        }

        return new ListToolsResult();
    };
}

if (slowStartMs > 0)
{
    // Hold the initialize request back without touching stdout: the protocol stream must stay
    // silent, while stderr carries the heartbeat that proves the process is still working.
    serverOptions.Filters.Message.IncomingFilters.Add(next =>
        async (context, cancellationToken) =>
        {
            if (context.JsonRpcMessage is JsonRpcRequest { Method: RequestMethods.Initialize })
            {
                await DelayStartAsync(cancellationToken);
            }

            await next(context, cancellationToken);
        }
    );
}

var server = McpServer.Create(new StdioServerTransport("test-mcp-server"), serverOptions);

await server.RunAsync();

async Task DelayStartAsync(CancellationToken cancellationToken)
{
    var remaining = slowStartMs;
    while (remaining > 0)
    {
        var slice = startHeartbeatMs > 0 ? Math.Min(startHeartbeatMs, remaining) : remaining;
        await Task.Delay(slice, cancellationToken);
        remaining -= slice;
        if (startHeartbeatMs > 0)
        {
            Console.Error.WriteLine("test-mcp-server: handshake in progress");
        }
    }
}

string Echo(string text) => text;

AddResult Add(int a, int b) => new(a + b);

async Task<string> SlowAsync(int seconds, CancellationToken cancellationToken)
{
    Mark("slow_started");
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
    }
    catch (OperationCanceledException)
    {
        Mark("slow_cancelled");
        throw;
    }

    return string.Create(CultureInfo.InvariantCulture, $"slept {seconds} seconds");
}

string Boom() => throw new InvalidOperationException("boom: the tool failed on purpose");

async Task<string> ElicitAsync(
    RequestContext<CallToolRequestParams> context,
    CancellationToken cancellationToken
)
{
    ElicitResult<ElicitationResponse> response =
        await context.Server.ElicitAsync<ElicitationResponse>(
            "Choose a plan.",
            cancellationToken: cancellationToken
        );
    string content = response.Content is null ? "empty" : "present";
    string result = $"action={response.Action};content={content}";
    Mark("elicitation " + result);
    return result;
}

async Task<string> ElicitFormAsync(
    RequestContext<CallToolRequestParams> context,
    CancellationToken cancellationToken
)
{
    bool form = context.Server.ClientCapabilities?.Elicitation?.Form is not null;
    bool url = context.Server.ClientCapabilities?.Elicitation?.Url is not null;
    Mark($"elicitation capability=form:{form};url:{url}");

    ElicitResult response = await context.Server.ElicitAsync(
        new ElicitRequestParams
        {
            Mode = "form",
            Message = "Choose settings for the generated project.",
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["display"] = new ElicitRequestParams.StringSchema
                    {
                        Title = "Display name",
                        Description = "Name shown in the project.",
                    },
                    ["count"] = new ElicitRequestParams.NumberSchema
                    {
                        Type = "integer",
                        Title = "Worker count",
                        Description = "Number of workers.",
                    },
                    ["enabled"] = new ElicitRequestParams.BooleanSchema
                    {
                        Title = "Enable feature",
                        Description = "Whether the feature is enabled.",
                    },
                    ["plan"] = new ElicitRequestParams.UntitledSingleSelectEnumSchema
                    {
                        Title = "Plan",
                        Description = "Choose the project plan.",
                        Enum = ["guided", "manual"],
                    },
                    ["tags"] = new ElicitRequestParams.UntitledMultiSelectEnumSchema
                    {
                        Title = "Tags",
                        Description = "Choose project tags.",
                        Items = new ElicitRequestParams.UntitledEnumItemsSchema
                        {
                            Type = "string",
                            Enum = ["safe", "fast"],
                        },
                    },
                },
                Required = ["display", "count", "enabled", "plan", "tags"],
            },
        },
        cancellationToken
    );
    string serializedContent = JsonSerializer.Serialize(response.Content);
    Mark($"elicitation response={response.Action};content={serializedContent}");
    return JsonSerializer.Serialize(new { response.Action, response.Content });
}

async Task<string> ElicitUrlAsync(
    RequestContext<CallToolRequestParams> context,
    CancellationToken cancellationToken
)
{
    try
    {
        ElicitResult response = await context.Server.ElicitAsync(
            new ElicitRequestParams
            {
                Mode = "url",
                ElicitationId = "test-url-elicitation",
                Url = "https://example.invalid/elicitation",
                Message = "Complete the external setup.",
            },
            cancellationToken
        );
        Mark($"url elicitation action={response.Action}");
        return $"url elicitation action={response.Action}";
    }
    catch (Exception exception)
    {
        string errorCode = exception is McpProtocolException protocolException
            ? protocolException.ErrorCode.ToString()
            : exception.GetType().Name;
        Mark($"url elicitation error={errorCode};{exception.Message}");
        return $"url elicitation error={errorCode};{exception.Message}";
    }
}

string SpawnTool()
{
    tools.Add(
        McpServerTool.Create(
            (Func<string>)(() => "late"),
            new McpServerToolCreateOptions
            {
                Name = "late_tool",
                Description = "Registered at runtime.",
            }
        )
    );
    return "registered late_tool";
}

internal sealed record AddResult(int Sum);

internal sealed class ElicitationResponse
{
    public string? Choice { get; set; }
}
