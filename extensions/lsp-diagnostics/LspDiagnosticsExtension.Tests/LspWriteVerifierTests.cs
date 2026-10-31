using Lunate.Agent;
using Lunate.Extensibility.Testing;
using Microsoft.Extensions.AI;

namespace LspDiagnosticsExtension.Tests;

/// <summary>
/// The issue's done-when for TypeScript write verification through the extension's write verifier:
/// the real loader, real write pipeline and a real fake-server process. A staged <c>.ts</c> write
/// that introduces an error is refused with the diagnostics as structured content; a slow, absent
/// or crashed server degrades to unverified with a reason — the write is never blocked; a clean
/// file verifies.
/// </summary>
public sealed class LspWriteVerifierTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_ts_write_that_introduces_an_error_is_refused_with_the_diagnostics()
    {
        using var temp = new TempDirectory();
        await using var host = TestSupport.CreateHost(temp);
        TestSupport.WriteSettings(host);
        var probe = new ProbeWriteTool();
        host.Tools.Add(probe);
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call-1",
                    "write-probe",
                    new Dictionary<string, object?>
                    {
                        ["path"] = "src/app.ts",
                        ["content"] = "let count: number = 'FIXME';\n",
                    }
                )
            )
            .Enqueue(ModelScripts.Text("given up"));

        await host.RunAsync(client, "write the file", Ct);

        Assert.NotNull(probe.Nested);
        Assert.True(probe.Nested!.IsError);
        Assert.Contains(
            "the language server reported 1 error",
            probe.Nested.Output,
            StringComparison.Ordinal
        );
        JsonElement structured = probe.Nested.StructuredContent!.Value;
        Assert.Equal("src/app.ts", structured.GetProperty("path").GetString());
        JsonElement diagnostic = structured.GetProperty("diagnostics")[0];
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
        Assert.Equal("fixme-check", diagnostic.GetProperty("code").GetString());
        Assert.Equal(
            "FIXME left on line 1",
            diagnostic.GetProperty("message").GetString(),
            ignoreCase: false
        );

        Assert.False(
            File.Exists(Path.Combine(host.WorkingDirectory, "src", "app.ts")),
            "a refused write must leave the file untouched"
        );
    }

    [Fact]
    public async Task A_ts_write_that_times_out_writes_the_file_and_reports_unverified_with_a_reason()
    {
        using var temp = new TempDirectory();
        await using var host = TestSupport.CreateHost(temp);
        TestSupport.WriteSettings(host, timeoutMs: 300, serverArgs: ["--silent"]);
        var client = new ScriptedModelClient()
            .Enqueue(ModelScripts.Write("call-1", "src/slow.ts", "export const value = 1;\n"))
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult run = await host.RunAsync(client, "write the file", Ct);

        ToolCallResult result = TestSupport.SingleToolResult(run);
        Assert.Contains(
            "unverified: the language server did not answer",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.True(
            File.Exists(Path.Combine(host.WorkingDirectory, "src", "slow.ts")),
            "a slow server degrades to unverified; it must not block the turn"
        );
    }

    [Fact]
    public async Task A_clean_ts_write_verifies_through_the_running_server()
    {
        using var temp = new TempDirectory();
        await using var host = TestSupport.CreateHost(temp);
        TestSupport.WriteSettings(host);
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Write("call-1", "src/clean.ts", "export const greeting = 'hello';\n")
            )
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult run = await host.RunAsync(client, "write the file", Ct);

        ToolCallResult result = TestSupport.SingleToolResult(run);
        Assert.Equal("wrote 1 lines to src/clean.ts (created)", result.Output);
        Assert.DoesNotContain("unverified", result.Output, StringComparison.Ordinal);
        Assert.True(
            File.Exists(Path.Combine(host.WorkingDirectory, "src", "clean.ts")),
            "a verified write lands"
        );
        Assert.True(TestSupport.LogContains(host, "verification of 'src/clean.ts': verified."));
    }

    [Fact]
    public async Task An_absent_server_writes_the_file_and_reports_unverified_with_a_reason()
    {
        using var temp = new TempDirectory();
        await using var host = TestSupport.CreateHost(temp);
        TestSupport.WriteSettings(host, serverCommand: "lsp-diagnostics-missing-server");
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Write("call-1", "src/gated.ts", "export const gate = 'closed';\n")
            )
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult run = await host.RunAsync(client, "write the file", Ct);

        ToolCallResult result = TestSupport.SingleToolResult(run);
        Assert.Contains(
            "unverified: the language server could not verify",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.True(
            File.Exists(Path.Combine(host.WorkingDirectory, "src", "gated.ts")),
            "an absent server degrades to unverified; it must not block the turn"
        );
    }

    [Fact]
    public async Task A_crashed_server_writes_the_file_and_reports_unverified()
    {
        using var temp = new TempDirectory();
        await using var host = TestSupport.CreateHost(temp);
        TestSupport.WriteSettings(host, serverArgs: ["--crash-after", "2"]);
        var client = new ScriptedModelClient()
            .Enqueue(ModelScripts.Write("call-1", "src/dies.ts", "export const crash = true;\n"))
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult run = await host.RunAsync(client, "write the file", Ct);

        ToolCallResult result = TestSupport.SingleToolResult(run);
        Assert.Contains(
            "unverified: the language server could not verify",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.True(
            File.Exists(Path.Combine(host.WorkingDirectory, "src", "dies.ts")),
            "a crashed server degrades to unverified; it must not block the turn"
        );
    }
}
