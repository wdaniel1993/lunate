using Lunate.Agent;
using Lunate.TestSupport;

namespace Lunate.Protocols.Tests;

internal sealed class NullAgentEvents : IAgentEvents
{
    public void Emit(AgentEvent agentEvent) { }
}

internal static class McpTestEnvironment
{
    public static ToolContext Context { get; } = new("unused", new NullAgentEvents());

    public static McpServerOptions Options(
        string markerPath,
        string name = "server",
        TimeSpan? callTimeout = null
    ) =>
        new(
            name,
            TestMcpServer.DotnetPath,
            ["exec", TestMcpServer.ServerDll],
            new Dictionary<string, string> { ["LUNATE_TEST_MCP_MARKER"] = markerPath },
            WorkingDirectory: null,
            callTimeout
        );
}
