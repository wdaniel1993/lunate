using System.Diagnostics;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Testing;
using Microsoft.Extensions.AI;

namespace LspDiagnosticsExtension.Tests;

/// <summary>
/// The sample's done-when: the real loader, harness, tools, hook runner and a real fake-server
/// process; the model is a local scripted client that records every request, so the diagnostics
/// text can be asserted on the exact next model request.
/// </summary>
public sealed class LspDiagnosticsExtensionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_write_with_a_TODO_attaches_diagnostics_and_the_next_request_carries_them()
    {
        using var temp = new TempDirectory();
        await using var host = TestSupport.CreateHost(temp);
        string marker = TestSupport.MarkerPath(host);
        TestSupport.WriteSettings(host, serverArgs: ["--marker", marker]);
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Write("call-1", "src/Foo.cs", "class Foo\n{\n    // TODO fix me\n}\n")
            )
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult run = await host.RunAsync(client, "write the file", Ct);

        ToolCallResult result = TestSupport.SingleToolResult(run);
        Assert.Contains(
            "lsp-diagnostics (dotnet): 1 diagnostic for src/Foo.cs",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "warning 3:1 TODO left on line 3 (todo-check)",
            result.Output,
            StringComparison.Ordinal
        );
        Assert.Single(TestSupport.MarkerLines(marker));

        ChatMessage toolMessage = client
            .Requests[1]
            .Single(message => message.Role == ChatRole.Tool);
        string nextRequest = string.Concat(
            toolMessage
                .Contents.OfType<FunctionResultContent>()
                .Select(content => content.Result?.ToString())
        );
        Assert.Contains(
            "warning 3:1 TODO left on line 3 (todo-check)",
            nextRequest,
            StringComparison.Ordinal
        );
        Assert.True(TestSupport.LogContains(host, "attached 1 diagnostics"));
    }

    [Fact]
    public async Task The_attached_diagnostics_are_structured_content_on_the_result()
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
                        ["path"] = "src/Foo.cs",
                        ["content"] = "// TODO fix\n",
                    }
                )
            )
            .Enqueue(ModelScripts.Text("done"));

        await host.RunAsync(client, "write the file", Ct);

        Assert.NotNull(probe.Nested);
        JsonElement structured = probe.Nested!.StructuredContent!.Value;
        Assert.Equal("src/Foo.cs", structured.GetProperty("path").GetString());
        JsonElement diagnostic = structured.GetProperty("diagnostics")[0];
        Assert.Equal("warning", diagnostic.GetProperty("severity").GetString());
        Assert.Equal(1, diagnostic.GetProperty("line").GetInt32());
        Assert.Equal(1, diagnostic.GetProperty("character").GetInt32());
        Assert.Equal("todo-check", diagnostic.GetProperty("code").GetString());
        Assert.Equal("TODO left on line 1", diagnostic.GetProperty("message").GetString());
    }

    [Fact]
    public async Task An_edit_uses_didChange_and_attaches_the_new_diagnostics()
    {
        using var temp = new TempDirectory();
        await using var host = TestSupport.CreateHost(temp);
        TestSupport.WriteSettings(host);
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Write("call-1", "src/Bar.cs", "class Bar\n{\n    // TODO seed\n}\n")
            )
            .Enqueue(ModelScripts.Text("done"))
            .Enqueue(
                ModelScripts.Edit(
                    "call-2",
                    "src/Bar.cs",
                    "// TODO seed",
                    "// TODO seed\n    // TODO added"
                )
            )
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult first = await host.RunAsync(client, "write", Ct);
        ExtensionRunResult second = await host.RunAsync(client, "edit", Ct);

        Assert.Contains(
            "1 diagnostic for src/Bar.cs",
            TestSupport.SingleToolResult(first).Output,
            StringComparison.Ordinal
        );
        string editedOutput = TestSupport.SingleToolResult(second).Output;
        Assert.Contains("2 diagnostics for src/Bar.cs", editedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("duplicate-open", editedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_non_cs_write_passes_through_and_never_starts_the_server()
    {
        using var temp = new TempDirectory();
        await using var host = TestSupport.CreateHost(temp);
        string marker = TestSupport.MarkerPath(host);
        TestSupport.WriteSettings(host, serverArgs: ["--marker", marker]);
        var client = new ScriptedModelClient()
            .Enqueue(ModelScripts.Write("call-1", "notes.txt", "TODO but not C#\n"))
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult run = await host.RunAsync(client, "write", Ct);

        ToolCallResult result = TestSupport.SingleToolResult(run);
        Assert.Equal("wrote 1 lines to notes.txt (created)", result.Output);
        Assert.False(File.Exists(marker));
        Assert.Empty(host.Log.Entries);
    }

    [Fact]
    public async Task A_stale_published_version_is_ignored()
    {
        using var temp = new TempDirectory();
        await using var host = TestSupport.CreateHost(temp);
        TestSupport.WriteSettings(host, serverArgs: ["--stale-first"]);
        var client = new ScriptedModelClient()
            .Enqueue(ModelScripts.Write("call-1", "src/Stale.cs", "// TODO fix\n"))
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult run = await host.RunAsync(client, "write", Ct);

        string output = TestSupport.SingleToolResult(run).Output;
        Assert.Contains("todo-check", output, StringComparison.Ordinal);
        Assert.DoesNotContain("stale-check", output, StringComparison.Ordinal);
    }

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
}
