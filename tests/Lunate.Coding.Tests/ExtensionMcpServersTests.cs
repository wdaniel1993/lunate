using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

public sealed class ExtensionMcpServersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_server_that_fails_to_start_logs_an_actionable_error_and_lists_nothing()
    {
        var log = new CollectingLog();
        await using var servers = new ExtensionMcpServers(
            log,
            [
                new RegisteredMcpServer(
                    "broken-extension",
                    new McpServerRegistration(
                        "broken",
                        new McpServerConfig("lunate-mcp-command-that-cannot-exist", [])
                    )
                ),
            ]
        );

        IReadOnlyList<ITool> tools = await servers.ListToolsAsync(Ct);

        Assert.Empty(tools);
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("'broken'", StringComparison.Ordinal)
                && message.Contains("broken-extension", StringComparison.Ordinal)
                && message.Contains("continues", StringComparison.Ordinal)
        );
    }

    private sealed class CollectingLog : IExtensionLog
    {
        public List<string> Messages { get; } = [];

        public void Info(string message) => Messages.Add($"info: {message}");

        public void Warn(string message) => Messages.Add($"warn: {message}");

        public void Error(string message) => Messages.Add($"error: {message}");
    }
}
