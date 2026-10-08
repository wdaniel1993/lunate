using System.Globalization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

// Tiny stdio MCP server for Lunate's protocol tests. stdio carries protocol only:
// markers go to the file named by LUNATE_TEST_MCP_MARKER, everything else to stderr.

var markerPath = Environment.GetEnvironmentVariable("LUNATE_TEST_MCP_MARKER");
var markerLock = new object();

void Mark(string line)
{
    if (string.IsNullOrEmpty(markerPath))
    {
        return;
    }

    lock (markerLock)
    {
        File.AppendAllText(markerPath, line + Environment.NewLine);
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
};
var server = McpServer.Create(new StdioServerTransport("test-mcp-server"), serverOptions);

await server.RunAsync();

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
