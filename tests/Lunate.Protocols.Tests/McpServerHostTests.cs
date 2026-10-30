using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Lunate.Agent;
using Lunate.TestSupport;

namespace Lunate.Protocols.Tests;

public sealed class McpServerHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Nothing_starts_before_first_use_and_the_first_listing_starts_once()
    {
        using var temp = new TempDirectory();
        var marker = temp.File("marker.txt");
        await using var host = new McpServerHost(McpTestEnvironment.Options(marker));

        Assert.False(host.Started);
        Assert.False(File.Exists(marker));

        var tools = await host.GetToolsAsync(Ct);

        Assert.True(host.Started);
        Assert.NotEmpty(tools);
        Assert.Single(StartedLines(marker));

        await host.GetToolsAsync(Ct);

        Assert.Single(StartedLines(marker));
    }

    [Fact]
    public async Task Tools_arrive_wrapped_with_prefix_schema_and_execute_risk()
    {
        using var temp = new TempDirectory();
        await using var host = new McpServerHost(
            McpTestEnvironment.Options(temp.File("marker.txt"))
        );

        var tools = await host.GetToolsAsync(Ct);

        Assert.Equal(
            [
                "server__add",
                "server__boom",
                "server__echo",
                "server__elicit_form",
                "server__slow",
                "server__spawn_tool",
            ],
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal)
        );

        foreach (var tool in tools)
        {
            Assert.Equal(ToolRisk.Execute, tool.Risk);
            Assert.Equal(JsonValueKind.Object, tool.ParametersSchema.ValueKind);
            Assert.True(tool.ParametersSchema.TryGetProperty("properties", out _));
        }

        var echo = tools.Single(tool => tool.Name == "server__echo");
        Assert.Contains("Echoes", echo.Description, StringComparison.Ordinal);
        Assert.True(echo.ParametersSchema.GetProperty("properties").TryGetProperty("text", out _));
        Assert.True(echo.Annotations is { ReadOnly: true });
        Assert.Equal(ToolRisk.Execute, echo.Risk);

        var boom = tools.Single(tool => tool.Name == "server__boom");
        Assert.Null(boom.Annotations);

        var add = tools.Single(tool => tool.Name == "server__add");
        var addProperties = add.ParametersSchema.GetProperty("properties");
        Assert.True(addProperties.TryGetProperty("a", out _));
        Assert.True(addProperties.TryGetProperty("b", out _));
    }

    [Fact]
    public async Task Spawn_tool_refreshes_the_tool_set_and_keeps_adapter_identity()
    {
        using var temp = new TempDirectory();
        // Collect every refresh instead of the first one: the server can emit more than one
        // tool-list change around a registration (the SDK sends list_changed per collection
        // change, fire-and-forget), so the assertion targets the refreshed set that contains
        // the new tool — and waits, bounded, for it to arrive.
        var refreshes = new List<IReadOnlyList<ITool>>();
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = new McpServerHost(
            McpTestEnvironment.Options(temp.File("marker.txt")),
            tools =>
            {
                lock (refreshes)
                {
                    refreshes.Add(tools);
                }

                if (tools.Any(tool => tool.Name == "server__late_tool"))
                {
                    seen.TrySetResult();
                }
            }
        );

        var first = await host.GetToolsAsync(Ct);
        var echoBefore = first.Single(tool => tool.Name == "server__echo");
        var spawn = first.Single(tool => tool.Name == "server__spawn_tool");

        var result = await spawn.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { }),
            McpTestEnvironment.Context,
            Ct
        );

        Assert.False(result.IsError);
        await seen.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        IReadOnlyList<ITool> updated;
        lock (refreshes)
        {
            updated = refreshes.First(tools => tools.Any(tool => tool.Name == "server__late_tool"));
        }

        Assert.Contains(updated, tool => tool.Name == "server__late_tool");
        Assert.Same(echoBefore, updated.Single(tool => tool.Name == "server__echo"));

        // The host is what GetToolsAsync answers from, so the refreshed set has to be the host's
        // set: a refresh that only reported the change would keep handing out the old tools.
        var relisted = await host.GetToolsAsync(Ct);
        Assert.Contains(relisted, tool => tool.Name == "server__late_tool");
        Assert.Same(
            updated.Single(tool => tool.Name == "server__echo"),
            relisted.Single(tool => tool.Name == "server__echo")
        );
    }

    [Fact]
    public async Task A_killed_server_becomes_error_results_and_the_host_stays_up()
    {
        using var temp = new TempDirectory();
        var marker = temp.File("marker.txt");
        await using var host = new McpServerHost(McpTestEnvironment.Options(marker));
        var tools = await host.GetToolsAsync(Ct);
        var echo = tools.Single(tool => tool.Name == "server__echo");
        var args = JsonSerializer.SerializeToElement(new { text = "hi" });

        var pid = TestMcpServer.StartedPid(marker);
        using (var process = Process.GetProcessById(pid))
        {
            process.Kill();
            await process.WaitForExitAsync(Ct);
        }

        await TestMcpServer.WaitForProcessExitAsync(pid, TimeSpan.FromSeconds(10));

        var result = await echo.ExecuteAsync(args, McpTestEnvironment.Context, Ct);

        Assert.True(result.IsError);
        Assert.Contains("server", result.Output, StringComparison.Ordinal);

        var again = await echo.ExecuteAsync(args, McpTestEnvironment.Context, Ct);

        Assert.True(again.IsError);
    }

    [Fact]
    public async Task Probe_reports_unresponsive_before_start_alive_after_start_and_exited_after_the_process_dies()
    {
        using var temp = new TempDirectory();
        var marker = temp.File("marker.txt");
        await using var host = new McpServerHost(McpTestEnvironment.Options(marker));

        Assert.Equal(McpProbeOutcome.Unresponsive, await host.ProbeAsync(Ct));

        await host.GetToolsAsync(Ct);

        Assert.Equal(McpProbeOutcome.Alive, await host.ProbeAsync(Ct));

        var pid = TestMcpServer.StartedPid(marker);
        using (var process = Process.GetProcessById(pid))
        {
            process.Kill();
            await process.WaitForExitAsync(Ct);
        }

        await TestMcpServer.WaitForProcessExitAsync(pid, TimeSpan.FromSeconds(10));

        Assert.Equal(McpProbeOutcome.Exited, await host.ProbeAsync(Ct));
    }

    [Fact]
    public async Task Probe_reports_unresponsive_when_the_server_misses_the_probe_bound()
    {
        using var temp = new TempDirectory();
        var marker = temp.File("marker.txt");
        var options = McpTestEnvironment.Options(marker) with
        {
            Environment = new Dictionary<string, string>
            {
                ["LUNATE_TEST_MCP_MARKER"] = marker,
                ["LUNATE_TEST_MCP_SLOW_LIST_MS"] = "2500",
            },
        };
        await using var host = new McpServerHost(options);

        Assert.NotEmpty(await host.GetToolsAsync(Ct));

        Assert.Equal(McpProbeOutcome.Unresponsive, await host.ProbeAsync(Ct));
    }

    [Fact]
    public async Task Dispose_stops_the_process_and_is_idempotent()
    {
        using var temp = new TempDirectory();
        var marker = temp.File("marker.txt");
        var host = new McpServerHost(McpTestEnvironment.Options(marker));
        var echo = (await host.GetToolsAsync(Ct)).Single(tool => tool.Name == "server__echo");
        var pid = TestMcpServer.StartedPid(marker);
        Assert.True(TestMcpServer.IsProcessAlive(pid));

        await host.DisposeAsync();

        await TestMcpServer.WaitForProcessExitAsync(pid, TimeSpan.FromSeconds(10));
        Assert.False(TestMcpServer.IsProcessAlive(pid));

        await host.DisposeAsync();

        var result = await echo.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { text = "hi" }),
            McpTestEnvironment.Context,
            Ct
        );

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task A_slow_server_that_keeps_talking_starts_instead_of_failing()
    {
        using var temp = new TempDirectory();
        var marker = temp.File("marker.txt");
        var stallBound = TimeSpan.FromSeconds(5);
        var options = McpTestEnvironment.Options(marker) with
        {
            Environment = new Dictionary<string, string>
            {
                ["LUNATE_TEST_MCP_MARKER"] = marker,
                ["LUNATE_TEST_MCP_SLOW_START_MS"] = "8000",
                ["LUNATE_TEST_MCP_START_HEARTBEAT_MS"] = "250",
            },
        };
        await using var host = new McpServerHost(options, stallBound);

        var elapsed = Stopwatch.StartNew();
        var tools = await host.GetToolsAsync(Ct);
        elapsed.Stop();

        Assert.NotEmpty(tools);
        Assert.True(host.Started);
        Assert.True(
            elapsed.Elapsed > stallBound,
            string.Create(
                CultureInfo.InvariantCulture,
                $"the handshake took {elapsed.Elapsed}, not more than the {stallBound} stall bound"
            )
        );
    }

    [Fact]
    public async Task A_hung_server_fails_once_no_output_arrives_within_the_stall_bound()
    {
        using var temp = new TempDirectory();
        var marker = temp.File("marker.txt");
        var stallBound = TimeSpan.FromSeconds(5);
        var options = McpTestEnvironment.Options(marker) with
        {
            Environment = new Dictionary<string, string>
            {
                ["LUNATE_TEST_MCP_MARKER"] = marker,
                ["LUNATE_TEST_MCP_SLOW_START_MS"] = "60000",
            },
        };
        await using var host = new McpServerHost(options, stallBound);

        var elapsed = Stopwatch.StartNew();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.GetToolsAsync(Ct).AsTask()
        );
        elapsed.Stop();

        Assert.Contains("stopped responding", exception.Message, StringComparison.Ordinal);
        Assert.True(
            elapsed.Elapsed < TimeSpan.FromSeconds(20),
            string.Create(
                CultureInfo.InvariantCulture,
                $"the start took {elapsed.Elapsed} before reporting the stall"
            )
        );
        Assert.False(host.Started);
    }

    private static IReadOnlyList<string> StartedLines(string markerPath) =>
        TestMcpServer
            .MarkerLines(markerPath)
            .Where(line => line.StartsWith("started ", StringComparison.Ordinal))
            .ToList();
}
