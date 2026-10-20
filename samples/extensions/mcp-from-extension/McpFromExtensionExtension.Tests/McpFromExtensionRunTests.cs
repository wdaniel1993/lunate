using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Testing;

namespace McpFromExtensionExtension.Tests;

/// <summary>
/// The done-when: the namespaced tools round-trip through the real harness, the model sees only
/// the declared ones, nested execution refuses the hidden one, failures stay isolated and a crash
/// flips the status exactly once.
/// </summary>
public sealed class McpFromExtensionRunTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Echo_is_declared_and_round_trips_through_approval()
    {
        using var temp = new TempDirectory();
        string marker = Path.Combine(temp.Root, "marker.txt");
        var approver = new ScriptedApprover(true);
        await using ExtensionTestHost host = TestSupport.CreateHost(temp, marker, approver);
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call-1",
                    "fake__echo",
                    new Dictionary<string, object?> { ["text"] = "hello mcp" }
                )
            )
            .Enqueue(ModelScripts.Text("done"));

        await host.ComposeMcpServersAsync(Ct);

        Assert.Contains(host.Tools.Declarations, declaration => declaration.Name == "fake__echo");
        Assert.DoesNotContain(
            host.Tools.Declarations,
            declaration => declaration.Name == "fake__secret"
        );
        Assert.Equal(
            ToolRisk.Execute,
            host.Tools.Tools.Single(tool => tool.Name == "fake__echo").Risk
        );

        ExtensionRunResult result = await host.RunAsync(client, "echo it", Ct);

        ToolCallResult echo = Assert.Single(result.Events.OfType<ToolCallResult>());
        Assert.False(echo.IsError);
        Assert.Equal("hello mcp", echo.Output);
        Assert.Equal(1, approver.Calls);
        Assert.Equal(1, approver.Approvals);
        Assert.Contains("echo hello mcp", TestSupport.MarkerLines(marker));
    }

    [Fact]
    public async Task Add_returns_structured_content_through_the_nested_executor()
    {
        using var temp = new TempDirectory();
        string marker = Path.Combine(temp.Root, "marker.txt");
        var approver = new ScriptedApprover(true, true);
        await using ExtensionTestHost host = TestSupport.CreateHost(temp, marker, approver);
        var probe = new NestedCallProbeTool();
        host.Tools.Add(probe);
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call-1",
                    "nested-probe",
                    new Dictionary<string, object?>
                    {
                        ["tool"] = "fake__add",
                        ["args"] = new Dictionary<string, object?> { ["a"] = 2, ["b"] = 3 },
                    }
                )
            )
            .Enqueue(ModelScripts.Text("done"));

        await host.ComposeMcpServersAsync(Ct);
        await host.RunAsync(client, "add through the probe", Ct);

        Assert.NotNull(probe.Nested);
        Assert.False(probe.Nested.IsError);
        JsonElement structured = probe.Nested.StructuredContent!.Value;
        Assert.Equal(5, structured.GetProperty("sum").GetInt32());
        Assert.Contains("add 2 3", TestSupport.MarkerLines(marker));
    }

    [Fact]
    public async Task Secret_is_neither_declared_nor_callable_via_nested_execution()
    {
        using var temp = new TempDirectory();
        string marker = Path.Combine(temp.Root, "marker.txt");
        var approver = new ScriptedApprover(true);
        await using ExtensionTestHost host = TestSupport.CreateHost(temp, marker, approver);
        var probe = new NestedCallProbeTool();
        host.Tools.Add(probe);
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call-1",
                    "nested-probe",
                    new Dictionary<string, object?>
                    {
                        ["tool"] = "fake__secret",
                        ["args"] = new Dictionary<string, object?>(),
                    }
                )
            )
            .Enqueue(ModelScripts.Text("done"));

        await host.ComposeMcpServersAsync(Ct);
        Assert.Contains(host.Tools.Tools, tool => tool.Name == "fake__secret");
        Assert.DoesNotContain(
            host.Tools.Declarations,
            declaration => declaration.Name == "fake__secret"
        );

        await host.RunAsync(client, "try the secret", Ct);

        Assert.NotNull(probe.Nested);
        Assert.True(probe.Nested.IsError);
        Assert.Contains(
            "not callable programmatically",
            probe.Nested.Output,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            TestSupport.MarkerLines(marker),
            line => line.Contains("secret", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_registration_with_a_missing_binary_is_logged_and_the_session_continues()
    {
        using var temp = new TempDirectory();
        string marker = Path.Combine(temp.Root, "marker.txt");
        await using ExtensionTestHost host = TestSupport.CreateHost(temp, marker);
        await host.StartAsync(Ct);
        host.Runner.McpServers.Register(
            "broken-extension",
            new McpServerRegistration(
                "broken",
                new McpServerConfig("lunate-mcp-command-that-cannot-exist", [])
            )
        );

        IReadOnlyList<ExtensionMcpServerStatus> statuses = await host.ComposeMcpServersAsync(Ct);

        Assert.Equal(
            ExtensionMcpServerState.Started,
            statuses.Single(status => status.Name == "fake").State
        );
        Assert.Equal(
            ExtensionMcpServerState.Failed,
            statuses.Single(status => status.Name == "broken").State
        );
        Assert.Contains(host.Tools.Tools, tool => tool.Name == "fake__echo");
        Assert.Contains(
            host.Log.Entries,
            entry =>
                entry.Message.Contains("'broken'", StringComparison.Ordinal)
                && entry.Message.Contains("broken-extension", StringComparison.Ordinal)
                && entry.Message.Contains("continues", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_crashed_server_errors_the_call_and_reads_failed_once()
    {
        using var temp = new TempDirectory();
        string marker = Path.Combine(temp.Root, "marker.txt");
        var approver = new ScriptedApprover(true);
        await using ExtensionTestHost host = TestSupport.CreateHost(temp, marker, approver);
        var client = new ScriptedModelClient()
            .Enqueue(ModelScripts.Call("call-1", "fake__crash", new Dictionary<string, object?>()))
            .Enqueue(ModelScripts.Text("done"));

        await host.ComposeMcpServersAsync(Ct);
        ExtensionRunResult result = await host.RunAsync(client, "crash it", Ct);

        ToolCallResult crash = Assert.Single(result.Events.OfType<ToolCallResult>());
        Assert.True(crash.IsError);

        // The crash surface reports the error immediately, but the server's state flips to
        // Failed when its process actually exits; under load that lands after the call.
        ExtensionMcpServerStatus status = await TestSupport.WaitForMcpStateAsync(
            host,
            ExtensionMcpServerState.Failed
        );

        Assert.Equal("the server process exited", status.Reason);
        Assert.Equal(1, host.Log.Count(ExtensionLogLevel.Error, "the server process exited"));

        IReadOnlyList<ExtensionMcpServerStatus> again = await host.ComposeMcpServersAsync(Ct);

        Assert.Equal(ExtensionMcpServerState.Failed, Assert.Single(again).State);
        Assert.Equal(1, host.Log.Count(ExtensionLogLevel.Error, "the server process exited"));
    }
}
