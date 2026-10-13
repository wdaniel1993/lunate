using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Testing;
using Microsoft.Extensions.AI;

namespace SubagentsExtension.Tests;

/// <summary>
/// The done-when through the real composition: the spawn tool runs a child agent, its events and
/// session link land on the parent, approvals route with the asking run, cancel propagates, the
/// limits surface as teaching refusals and hooks carry the child's run ids.
/// </summary>
public sealed class SubagentsRunTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static string LastToolResult(ScriptedModelClient client, int requestIndex) =>
        Assert
            .Single(client.Requests[requestIndex][^1].Contents.OfType<FunctionResultContent>())
            .Result!.ToString()!;

    [Fact]
    public async Task A_spawn_links_the_child_session_and_returns_its_answer()
    {
        using var temp = new TempDirectory();
        var approver = new ScriptedApprover(true);
        await using ExtensionTestHost host = TestSupport.CreateHost(temp, approver);
        await host.StartAsync(Ct);
        host.ComposeTools();
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "subagents__spawn",
                    ModelScripts.Args(("prompt", "child prompt"))
                )
            )
            .Enqueue(ModelScripts.Text("child answer"), ModelScripts.Usage(10, 5))
            .Enqueue(ModelScripts.Text("parent done"));

        ExtensionRunResult result = await host.RunAsync(client, "go", Ct);

        string parentRunId = result.Events.OfType<RunStarted>().First().RunId;
        RunStarted childStart = result
            .Events.OfType<RunStarted>()
            .Single(start => start.RunId != parentRunId);
        Assert.Equal(parentRunId, childStart.ParentRunId);
        Assert.Contains(
            result.Events.OfType<UsageUpdated>(),
            usage => usage.ParentRunId == parentRunId
        );
        Assert.Equal(StopReasons.Stop, result.Events.OfType<RunFinished>().Last().StopReason);
        Assert.Equal("child answer", LastToolResult(client, 2));

        Session session = Session.Load(result.SessionPath);
        SessionChildSessionEntry link = Assert.Single(
            session.Entries.OfType<SessionChildSessionEntry>()
        );
        Assert.Equal(childStart.RunId, link.RunId);
        string childPath = TestSupport.ChildSessionPath(result.SessionPath, link.ChildSessionId);
        Assert.True(File.Exists(childPath), $"the child session '{childPath}' should exist");
        Assert.Equal("child prompt", Session.Load(childPath).ToHistory()[0].Text);
        Assert.Equal(1, approver.Calls);
        Assert.Equal(1, approver.Approvals);
    }

    [Fact]
    public async Task A_child_cannot_spawn_and_the_refusal_names_the_limit()
    {
        using var temp = new TempDirectory();
        var approver = new ScriptedApprover(true, true);
        await using ExtensionTestHost host = TestSupport.CreateHost(temp, approver);
        await host.StartAsync(Ct);
        host.ComposeTools();
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "subagents__spawn",
                    ModelScripts.Args(("prompt", "child prompt"))
                )
            )
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "subagents__spawn",
                    ModelScripts.Args(("prompt", "grandchild prompt"))
                )
            )
            .Enqueue(ModelScripts.Text("child done"))
            .Enqueue(ModelScripts.Text("parent done"));

        ExtensionRunResult result = await host.RunAsync(client, "go", Ct);

        string refusal = LastToolResult(client, 2);
        Assert.Contains("depth_limit", refusal, StringComparison.Ordinal);
        Assert.Contains("fixed at 1", refusal, StringComparison.Ordinal);
        Assert.Equal(4, client.Requests.Count);
        Session session = Session.Load(result.SessionPath);
        Assert.Single(session.Entries.OfType<SessionChildSessionEntry>());
    }

    [Fact]
    public async Task A_childs_ask_reaches_the_parent_approver_with_its_run_context()
    {
        using var temp = new TempDirectory();
        var approver = new RecordingApprover(name => name != "child-probe");
        await using ExtensionTestHost host = TestSupport.CreateHost(temp, approver);
        await host.StartAsync(Ct);
        host.ComposeTools();
        host.Tools.Add(new TestProbeTool());
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "subagents__spawn",
                    ModelScripts.Args(("prompt", "child prompt"))
                )
            )
            .Enqueue(ModelScripts.Call("call_1", "child-probe", ModelScripts.Args()))
            .Enqueue(ModelScripts.Text("child done"))
            .Enqueue(ModelScripts.Text("parent done"));

        ExtensionRunResult result = await host.RunAsync(client, "go", Ct);

        string parentRunId = result.Events.OfType<RunStarted>().First().RunId;
        string childRunId = result
            .Events.OfType<RunStarted>()
            .Single(start => start.RunId != parentRunId)
            .RunId;
        ApprovalRequest spawnAsk = Assert.Single(
            approver.Requests,
            request => request.Tool.Name == "subagents__spawn"
        );
        Assert.Equal(parentRunId, spawnAsk.RunId);
        Assert.Equal(0, spawnAsk.Depth);
        ApprovalRequest childAsk = Assert.Single(
            approver.Requests,
            request => request.Tool.Name == "child-probe"
        );
        Assert.Equal(childRunId, childAsk.RunId);
        Assert.Equal(parentRunId, childAsk.ParentRunId);
        Assert.Equal(1, childAsk.Depth);
        Assert.Contains("Denied", LastToolResult(client, 2), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelling_the_parent_run_cancels_the_child_and_joins()
    {
        using var temp = new TempDirectory();
        var approver = new ScriptedApprover(true);
        await using ExtensionTestHost host = TestSupport.CreateHost(temp, approver);
        await host.StartAsync(Ct);
        host.ComposeTools();
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "subagents__spawn",
                    ModelScripts.Args(("prompt", "child prompt"))
                )
            )
            .Enqueue(ModelScripts.Text("never reached"));
        client.Gate(2);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        Task<ExtensionRunResult> run = host.RunAsync(client, "go", cancellation.Token);

        await client.WaitForCallAsync(2);
        cancellation.Cancel();
        try
        {
            await run.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        }
        catch (OperationCanceledException)
        {
            // The host's drain observes the cancelled token; the events are already recorded.
        }

        Assert.Contains(
            host.Events.Events.OfType<RunFinished>(),
            finished =>
                finished.ParentRunId is not null && finished.StopReason == StopReasons.Cancelled
        );
        Assert.Contains(
            host.Events.Events.OfType<RunFinished>(),
            finished => finished.ParentRunId is null && finished.StopReason == StopReasons.Cancelled
        );
    }

    [Fact]
    public async Task An_over_budget_child_stops_with_its_partial_result()
    {
        using var temp = new TempDirectory();
        var approver = new ScriptedApprover(true, true);
        await using ExtensionTestHost host = TestSupport.CreateHost(
            temp,
            approver,
            limits: new SubagentLimits(Concurrency: 4, TokenBudget: 10, TreeTokenBudget: 1000)
        );
        await host.StartAsync(Ct);
        host.ComposeTools();
        host.Tools.Add(new TestProbeTool());
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "subagents__spawn",
                    ModelScripts.Args(("prompt", "child prompt"))
                )
            )
            .Enqueue(
                ModelScripts.Text("partial answer"),
                ModelScripts.Usage(6, 6),
                ModelScripts.Call("call_1", "child-probe", ModelScripts.Args())
            )
            .Enqueue(ModelScripts.Text("parent done"));

        ExtensionRunResult result = await host.RunAsync(client, "go", Ct);

        Assert.Equal(3, client.Requests.Count);
        Assert.Equal("partial answer", LastToolResult(client, 2));
        Assert.Contains(
            result.Events.OfType<RunFinished>(),
            finished =>
                finished.ParentRunId is not null && finished.StopReason == StopReasons.TokenLimit
        );
    }

    [Fact]
    public async Task A_spawn_while_the_tree_concurrency_is_held_is_refused()
    {
        using var temp = new TempDirectory();
        var approver = new ScriptedApprover(true, true);
        await using ExtensionTestHost host = TestSupport.CreateHost(
            temp,
            approver,
            limits: new SubagentLimits(Concurrency: 1)
        );
        await host.StartAsync(Ct);
        host.ComposeTools();
        var client = new ScriptedModelClient()
            .Enqueue(ModelScripts.Call("call_1", "concurrent-probe", ModelScripts.Args()))
            .Enqueue(ModelScripts.Text("one done"))
            .Enqueue(ModelScripts.Text("parent done"));
        client.Gate(2);
        var probe = new ConcurrentSpawnProbe(client);
        host.Tools.Add(probe);

        ExtensionRunResult result = await host.RunAsync(client, "go", Ct);

        Assert.NotNull(probe.Refusal);
        Assert.NotNull(probe.Completed);
        Assert.Equal(StopReasons.Stop, probe.Completed.StopReason);
        Assert.Equal(StopReasons.ConcurrencyLimit, probe.Refusal.StopReason);
        Assert.Contains("subagents.concurrency", probe.Refusal.Output, StringComparison.Ordinal);
        Assert.Equal(StopReasons.Stop, result.Events.OfType<RunFinished>().Last().StopReason);
    }

    [Fact]
    public async Task Child_run_hook_payloads_carry_both_run_ids()
    {
        using var temp = new TempDirectory();
        var approver = new ScriptedApprover(true, true);
        var hooks = new RecordingHooks();
        await using ExtensionTestHost host = TestSupport.CreateHost(
            temp,
            approver,
            configure: options => options with { Hooks = hooks }
        );
        await host.StartAsync(Ct);
        host.ComposeTools();
        host.Tools.Add(new TestProbeTool());
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "subagents__spawn",
                    ModelScripts.Args(("prompt", "child prompt"))
                )
            )
            .Enqueue(ModelScripts.Call("call_1", "child-probe", ModelScripts.Args()))
            .Enqueue(ModelScripts.Text("child done"))
            .Enqueue(ModelScripts.Text("parent done"));

        ExtensionRunResult result = await host.RunAsync(client, "go", Ct);

        string parentRunId = result.Events.OfType<RunStarted>().First().RunId;
        string childRunId = result
            .Events.OfType<RunStarted>()
            .Single(start => start.RunId != parentRunId)
            .RunId;
        AgentToolCallingContext childCalling = Assert.Single(
            hooks.ToolCallings,
            context => context.RunId == childRunId
        );
        Assert.Equal(parentRunId, childCalling.ParentRunId);
        Assert.Null(
            Assert.Single(hooks.ToolCallings, context => context.RunId == parentRunId).ParentRunId
        );
    }

    /// <summary>
    /// A programmatic caller that spawns twice concurrently through the run's own runner: the first
    /// child is held live (gated model call), the second spawn must be refused immediately.
    /// </summary>
    private sealed class ConcurrentSpawnProbe(ScriptedModelClient client) : ITool
    {
        public SubagentResult? Refusal { get; private set; }

        public SubagentResult? Completed { get; private set; }

        public string Name => "concurrent-probe";

        public string Description => "Spawns two subagents concurrently for the concurrency test.";

        public JsonElement ParametersSchema { get; } =
            JsonSerializer.SerializeToElement(new { type = "object", properties = new { } });

        public ToolRisk Risk => ToolRisk.Write;

        public async Task<ToolResult> ExecuteAsync(
            JsonElement args,
            ToolContext ctx,
            CancellationToken ct
        )
        {
            ISubagentRunner runner = ctx.Subagents!;
            Task<SubagentResult> first = runner.RunAsync(new SubagentRequest("one"), ct);
            await client.WaitForCallAsync(2);
            SubagentResult second = await runner.RunAsync(new SubagentRequest("two"), ct);
            client.Release(2);
            SubagentResult completed = await first;
            Refusal = second;
            Completed = completed;
            return new ToolResult(second.Output, second.IsError);
        }
    }
}
