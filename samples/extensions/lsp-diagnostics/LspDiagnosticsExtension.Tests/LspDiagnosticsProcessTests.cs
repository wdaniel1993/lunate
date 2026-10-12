using System.Diagnostics;
using Lunate.Agent;
using Lunate.Extensibility.Testing;
using Microsoft.Extensions.AI;

namespace LspDiagnosticsExtension.Tests;

/// <summary>
/// Process ownership: timeouts keep the original result and restart on the next call, crashes
/// restart the server, and one session starts the fake once and stops it once.
/// </summary>
public sealed class LspDiagnosticsProcessTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_timeout_keeps_the_result_and_the_next_call_restarts_the_server()
    {
        using var temp = new TempDirectory();
        await using var host = TestSupport.CreateHost(temp);
        string marker = TestSupport.MarkerPath(host);
        TestSupport.WriteSettings(
            host,
            timeoutMs: 300,
            serverArgs: ["--silent", "--marker", marker]
        );
        var client = new ScriptedModelClient()
            .Enqueue(ModelScripts.Write("call-1", "src/One.cs", "// TODO one\n"))
            .Enqueue(ModelScripts.Text("done"))
            .Enqueue(ModelScripts.Write("call-2", "src/Two.cs", "// TODO two\n"))
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult first = await host.RunAsync(client, "write one", Ct);
        ExtensionRunResult second = await host.RunAsync(client, "write two", Ct);

        Assert.DoesNotContain(
            "lsp-diagnostics",
            TestSupport.SingleToolResult(first).Output,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "lsp-diagnostics",
            TestSupport.SingleToolResult(second).Output,
            StringComparison.Ordinal
        );
        Assert.Equal(2, TestSupport.MarkerLines(marker).Length);
        Assert.Equal(2, TestSupport.LogCount(host, ExtensionLogLevel.Error));
        Assert.True(TestSupport.LogContains(host, "did not answer"));
    }

    [Fact]
    public async Task A_crash_restarts_the_server_and_the_failure_is_logged()
    {
        using var temp = new TempDirectory();
        await using var host = TestSupport.CreateHost(temp);
        string marker = TestSupport.MarkerPath(host);
        TestSupport.WriteSettings(host, serverArgs: ["--crash-after", "2", "--marker", marker]);
        var client = new ScriptedModelClient()
            .Enqueue(ModelScripts.Write("call-1", "src/One.cs", "// TODO one\n"))
            .Enqueue(ModelScripts.Text("done"))
            .Enqueue(ModelScripts.Write("call-2", "src/Two.cs", "// TODO two\n"))
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult first = await host.RunAsync(client, "write one", Ct);
        ExtensionRunResult second = await host.RunAsync(client, "write two", Ct);

        Assert.DoesNotContain(
            "lsp-diagnostics",
            TestSupport.SingleToolResult(first).Output,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "lsp-diagnostics",
            TestSupport.SingleToolResult(second).Output,
            StringComparison.Ordinal
        );
        Assert.Equal(2, TestSupport.MarkerLines(marker).Length);
        Assert.Equal(2, TestSupport.LogCount(host, ExtensionLogLevel.Error));
        Assert.True(TestSupport.LogContains(host, "the tool result is unchanged"));
    }

    [Fact]
    public async Task The_server_starts_once_and_stops_once_at_session_end()
    {
        using var temp = new TempDirectory();
        await using var host = TestSupport.CreateHost(temp);
        string marker = TestSupport.MarkerPath(host);
        TestSupport.WriteSettings(host, serverArgs: ["--marker", marker]);
        var client = new ScriptedModelClient()
            .Enqueue(ModelScripts.Write("call-1", "src/One.cs", "// TODO one\n"))
            .Enqueue(ModelScripts.Text("done"))
            .Enqueue(ModelScripts.Write("call-2", "src/Two.cs", "// TODO two\n"))
            .Enqueue(ModelScripts.Text("done"));

        await host.RunAsync(client, "write one", Ct);
        await host.RunAsync(client, "write two", Ct);

        Assert.Single(TestSupport.MarkerLines(marker));
        int pid = TestSupport.MarkerPid(marker);
        Assert.Equal(1, TestSupport.LogCount(host, ExtensionLogLevel.Info, "started 'dotnet'"));

        await host.StopAsync(Ct);

        Assert.Equal(
            1,
            TestSupport.LogCount(host, ExtensionLogLevel.Info, "stopped the language server")
        );
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));

        await host.StopAsync(Ct);
        await host.DisposeAsync();

        Assert.Equal(
            1,
            TestSupport.LogCount(host, ExtensionLogLevel.Info, "stopped the language server")
        );
    }
}
