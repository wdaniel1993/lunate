using Lunate.Agent;
using Lunate.Extensibility.Testing;
using Microsoft.Extensions.AI;

namespace LspDiagnosticsExtension.Tests;

/// <summary>
/// Shaping and pass-through: only errors and warnings attach, the cap adds the `+n more` line,
/// information and hints are excluded, and unrelated tool calls never start the server.
/// </summary>
public sealed class LspDiagnosticsShapingTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Errors_and_warnings_are_capped_and_info_and_hints_are_excluded()
    {
        using var temp = new TempDirectory();
        await using var host = TestSupport.CreateHost(temp);
        TestSupport.WriteSettings(host, maxDiagnostics: 2);
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Write(
                    "call-1",
                    "src/Many.cs",
                    "// FIXME one\n// TODO two\n// TODO three\n// TODO four\n// TODO five\n// INFO note\n// HINT tip\n"
                )
            )
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult run = await host.RunAsync(client, "write", Ct);

        string output = TestSupport.SingleToolResult(run).Output;
        Assert.Contains(
            "lsp-diagnostics (dotnet): 5 diagnostics for src/Many.cs",
            output,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "error 1:1 FIXME left on line 1 (fixme-check)",
            output,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "warning 2:1 TODO left on line 2 (todo-check)",
            output,
            StringComparison.Ordinal
        );
        Assert.Contains("+3 more diagnostics", output, StringComparison.Ordinal);
        Assert.DoesNotContain("info-check", output, StringComparison.Ordinal);
        Assert.DoesNotContain("hint-check", output, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO left on line 3", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_information_and_hints_attach_nothing_and_are_logged()
    {
        using var temp = new TempDirectory();
        await using var host = TestSupport.CreateHost(temp);
        TestSupport.WriteSettings(host);
        var client = new ScriptedModelClient()
            .Enqueue(ModelScripts.Write("call-1", "src/Quiet.cs", "// INFO note\n// HINT tip\n"))
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult run = await host.RunAsync(client, "write", Ct);

        string output = TestSupport.SingleToolResult(run).Output;
        Assert.Equal("wrote 2 lines to src/Quiet.cs (created)", output);
        Assert.True(TestSupport.LogContains(host, "no errors or warnings"));
    }

    [Fact]
    public async Task Reading_a_cs_file_passes_through_without_starting_the_server()
    {
        using var temp = new TempDirectory();
        await using var host = TestSupport.CreateHost(temp);
        string marker = TestSupport.MarkerPath(host);
        TestSupport.WriteSettings(host, serverArgs: ["--marker", marker]);
        Directory.CreateDirectory(Path.Combine(host.WorkingDirectory, "src"));
        File.WriteAllText(
            Path.Combine(host.WorkingDirectory, "src", "Read.cs"),
            "// TODO present\n"
        );
        var client = new ScriptedModelClient()
            .Enqueue(ModelScripts.Read("call-1", "src/Read.cs"))
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult run = await host.RunAsync(client, "read", Ct);

        string output = TestSupport.SingleToolResult(run).Output;
        Assert.Contains("TODO present", output, StringComparison.Ordinal);
        Assert.DoesNotContain("lsp-diagnostics", output, StringComparison.Ordinal);
        Assert.False(File.Exists(marker));
        Assert.Empty(host.Log.Entries);
    }

    [Fact]
    public async Task A_failed_edit_passes_through_without_starting_the_server()
    {
        using var temp = new TempDirectory();
        await using var host = TestSupport.CreateHost(temp);
        string marker = TestSupport.MarkerPath(host);
        TestSupport.WriteSettings(host, serverArgs: ["--marker", marker]);
        Directory.CreateDirectory(Path.Combine(host.WorkingDirectory, "src"));
        File.WriteAllText(
            Path.Combine(host.WorkingDirectory, "src", "Unchanged.cs"),
            "// TODO present\n"
        );
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Edit(
                    "call-1",
                    "src/Unchanged.cs",
                    "// TODO absent",
                    "// TODO replacement"
                )
            )
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult run = await host.RunAsync(client, "edit", Ct);

        ToolCallResult result = TestSupport.SingleToolResult(run);
        Assert.True(result.IsError);
        Assert.DoesNotContain("lsp-diagnostics", result.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(marker));
        Assert.Empty(host.Log.Entries);
    }
}
