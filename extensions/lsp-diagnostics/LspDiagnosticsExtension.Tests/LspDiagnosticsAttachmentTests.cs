using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Testing;
using Microsoft.Extensions.AI;

namespace LspDiagnosticsExtension.Tests;

/// <summary>
/// The sample's done-when for attachment: the real loader, harness, tools, hook runner and a
/// real fake-server process; the model is a local scripted client that records every request, so
/// the diagnostics text can be asserted on the exact next model request.
/// </summary>
public sealed class LspDiagnosticsAttachmentTests
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
}
