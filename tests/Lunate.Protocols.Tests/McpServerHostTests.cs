using System.Diagnostics;
using System.Text.Json;
using Lunate.Agent;

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
            ["server__add", "server__boom", "server__echo", "server__slow", "server__spawn_tool"],
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

        var pid = McpTestEnvironment.StartedPid(marker);
        using (var process = Process.GetProcessById(pid))
        {
            process.Kill();
            await process.WaitForExitAsync(Ct);
        }

        await McpTestEnvironment.WaitForProcessExitAsync(pid, TimeSpan.FromSeconds(10));

        var result = await echo.ExecuteAsync(args, McpTestEnvironment.Context, Ct);

        Assert.True(result.IsError);
        Assert.Contains("server", result.Output, StringComparison.Ordinal);

        var again = await echo.ExecuteAsync(args, McpTestEnvironment.Context, Ct);

        Assert.True(again.IsError);
    }

    [Fact]
    public async Task Dispose_stops_the_process_and_is_idempotent()
    {
        using var temp = new TempDirectory();
        var marker = temp.File("marker.txt");
        var host = new McpServerHost(McpTestEnvironment.Options(marker));
        var echo = (await host.GetToolsAsync(Ct)).Single(tool => tool.Name == "server__echo");
        var pid = McpTestEnvironment.StartedPid(marker);
        Assert.True(McpTestEnvironment.IsProcessAlive(pid));

        await host.DisposeAsync();

        await McpTestEnvironment.WaitForProcessExitAsync(pid, TimeSpan.FromSeconds(10));
        Assert.False(McpTestEnvironment.IsProcessAlive(pid));

        await host.DisposeAsync();

        var result = await echo.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { text = "hi" }),
            McpTestEnvironment.Context,
            Ct
        );

        Assert.True(result.IsError);
    }

    private static IReadOnlyList<string> StartedLines(string markerPath) =>
        McpTestEnvironment
            .MarkerLines(markerPath)
            .Where(line => line.StartsWith("started ", StringComparison.Ordinal))
            .ToList();
}
