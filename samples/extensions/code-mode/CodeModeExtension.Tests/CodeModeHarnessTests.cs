using Lunate.Agent;
using Lunate.Coding;
using Lunate.Extensibility.Testing;

namespace CodeModeExtension.Tests;

/// <summary>
/// The bridge through the real composition: script calls of a Programmatic fixture and a Direct
/// tool run through the core pipeline with canonical nested ids and session entries, a declined
/// destructive ask becomes a catchable error, and an "always" grant on the outer tool does not
/// suppress a later bridged ask.
/// </summary>
public sealed class CodeModeHarnessTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private const string BridgeScript = """
        console.log("start");
        const a = await tools["prog-fixture"]({ value: "xyz" });
        const b = await tools["read"]({ path: "note.txt" });
        return a + "/" + b;
        """;

    [Fact]
    public async Task A_script_calls_programmatic_and_core_tools_through_the_bridge()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp);
        await host.StartAsync(Ct);
        host.ComposeTools();
        host.Tools.Add(new EchoFixtureTool());
        host.Tools.Add(new ReadTool(new Workspace(host.WorkingDirectory)));
        File.WriteAllText(Path.Combine(host.WorkingDirectory, "note.txt"), "from-disk\n");
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "codemode__run",
                    ModelScripts.Args(("code", BridgeScript))
                )
            )
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult result = await host.RunAsync(client, "go", Ct);

        // The Programmatic fixture is never declared to the model; the script still calls it.
        Assert.Equal(["codemode__run", "read"], client.Declarations[0]);
        string toolResult = TestSupport.LastToolResult(client, 1);
        Assert.StartsWith("prog:xyz/", toolResult, StringComparison.Ordinal);
        Assert.Contains("from-disk", toolResult, StringComparison.Ordinal);

        // Nested calls carry the canonical ids and their parent.
        var nestedStarts = result
            .Events.OfType<ToolCallStart>()
            .Where(start => start.CallId.Contains('/', StringComparison.Ordinal))
            .ToList();
        Assert.Equal(["call_1/1", "call_1/2"], nestedStarts.Select(start => start.CallId));
        Assert.All(nestedStarts, start => Assert.Equal("call_1", start.ParentToolCallId));

        // The session carries one nested-call record for the outer call.
        Session session = Session.Load(result.SessionPath);
        SessionNestedCallsEntry entry = Assert.Single(
            session.Entries.OfType<SessionNestedCallsEntry>()
        );
        Assert.Equal("call_1", entry.CallId);
        Assert.Equal(["prog-fixture", "read"], entry.Calls.Select(call => call.Name));
        Assert.All(entry.Calls, call => Assert.Equal("ok", call.Status));
    }

    [Fact]
    public async Task A_declined_destructive_nested_call_is_a_catchable_error()
    {
        using var temp = new TempDirectory();
        var approver = new ScriptedApprover(true, false);
        await using ExtensionTestHost host = TestSupport.CreateHost(temp, approver);
        await host.StartAsync(Ct);
        host.ComposeTools();
        host.Tools.Add(new FixtureTool("destructive-fixture", destructive: true));
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "codemode__run",
                    ModelScripts.Args(
                        (
                            "code",
                            """
                            try {
                              await tools["destructive-fixture"]({});
                              return "ran";
                            } catch (error) {
                              return "caught:" + error.message;
                            }
                            """
                        )
                    )
                )
            )
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult result = await host.RunAsync(client, "go", Ct);

        string toolResult = TestSupport.LastToolResult(client, 1);
        Assert.Contains("caught:", toolResult, StringComparison.Ordinal);
        Assert.Contains("Denied", toolResult, StringComparison.Ordinal);
        Assert.Equal(2, approver.Calls);
        Assert.Equal(1, approver.Denials);
        SessionNestedCallsEntry entry = Assert.Single(
            Session.Load(result.SessionPath).Entries.OfType<SessionNestedCallsEntry>()
        );
        Assert.Equal("error", Assert.Single(entry.Calls).Status);
    }

    [Fact]
    public async Task An_always_grant_on_the_outer_tool_does_not_suppress_a_bridged_ask()
    {
        using var temp = new TempDirectory();
        var approver = new AlwaysApprover("codemode__run").Decide(false);
        await using ExtensionTestHost host = TestSupport.CreateHost(temp, approver);
        await host.StartAsync(Ct);
        host.ComposeTools();
        host.Tools.Add(new FixtureTool("destructive-fixture", destructive: true));
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "codemode__run",
                    ModelScripts.Args(
                        (
                            "code",
                            """
                            try {
                              await tools["destructive-fixture"]({});
                              return "ran";
                            } catch (error) {
                              return "caught";
                            }
                            """
                        )
                    )
                )
            )
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult result = await host.RunAsync(client, "go", Ct);

        // The always grant covers codemode__run silently; the bridged destructive call still asked
        // and its decline reached the script.
        ApprovalRequest ask = Assert.Single(approver.Requests);
        Assert.Equal("destructive-fixture", ask.Tool.Name);
        Assert.Equal("caught", TestSupport.LastToolResult(client, 1));
        Assert.Single(Session.Load(result.SessionPath).Entries.OfType<SessionNestedCallsEntry>());
    }

    [Fact]
    public async Task A_timeout_cancels_a_pending_bridged_call_and_the_tool_never_runs()
    {
        using var temp = new TempDirectory();
        var approver = new BlockingApprover("destructive-fixture");
        await using ExtensionTestHost host = TestSupport.CreateHost(
            temp,
            approver,
            timeoutSeconds: 8
        );
        await host.StartAsync(Ct);
        host.ComposeTools();
        var fixture = new FixtureTool("destructive-fixture", destructive: true);
        host.Tools.Add(fixture);
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "codemode__run",
                    ModelScripts.Args(
                        (
                            "code",
                            """
                            console.log(process.pid);
                            await tools["destructive-fixture"]({});
                            return "done";
                            """
                        )
                    )
                )
            )
            .Enqueue(ModelScripts.Text("done"));

        ExtensionRunResult result = await host.RunAsync(client, "go", Ct);

        // The child timed out while the bridged ask was pending: the run reported it, and the
        // pending call was cancelled with the child.
        string toolResult = TestSupport.LastToolResult(client, 1);
        Assert.Contains("timed out", toolResult, StringComparison.Ordinal);
        Assert.True(approver.Asked.Task.IsCompleted);
        // A late approval must not run the tool: the ask was cancelled with the child.
        approver.Release(approved: true);
        await Task.Delay(500, Ct);
        Assert.Equal(0, fixture.Calls);
    }
}
