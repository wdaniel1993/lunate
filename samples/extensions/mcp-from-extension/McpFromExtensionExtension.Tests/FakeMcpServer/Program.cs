using System.Globalization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

// Deterministic stdio MCP server for the mcp-from-extension sample. stdio carries protocol only:
// markers go to the file named by LUNATE_FAKE_MCP_MARKER, everything else to stderr.

var markerPath = Environment.GetEnvironmentVariable("LUNATE_FAKE_MCP_MARKER");
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
            Description = "Adds two integers and returns the sum as structured content.",
            UseStructuredContent = true,
        }
    )
);
tools.Add(
    McpServerTool.Create(
        (Func<string>)Secret,
        new McpServerToolCreateOptions
        {
            Name = "secret",
            Description = "Returns the extension's secret payload; hidden by an exposure override.",
        }
    )
);
tools.Add(
    McpServerTool.Create(
        (Func<string>)Crash,
        new McpServerToolCreateOptions
        {
            Name = "crash",
            Description = "Exits the server process; the liveness story.",
        }
    )
);

var serverOptions = new McpServerOptions
{
    ServerInfo = new Implementation { Name = "fake-mcp-server", Version = "1.0.0" },
    ToolCollection = tools,
};
var server = McpServer.Create(new StdioServerTransport("fake-mcp-server"), serverOptions);

await server.RunAsync();

string Echo(string text)
{
    Mark("echo " + text);
    return text;
}

AddResult Add(int a, int b)
{
    Mark(string.Create(CultureInfo.InvariantCulture, $"add {a} {b}"));
    return new AddResult(a + b);
}

string Secret()
{
    Mark("secret");
    return "the secret payload";
}

string Crash()
{
    Mark("crash");
    Environment.Exit(1);
    throw new InvalidOperationException("the process exited");
}

internal sealed record AddResult(int Sum);
