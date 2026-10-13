using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

/// <summary>The subagent runner core: child composition, linking, forwarding, cancellation and hooks.</summary>
public sealed class SubagentRunnerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static ScriptedTool SpawnTool(
        Func<ToolContext, CancellationToken, Task<SubagentResult>> run,
        string name = "spawn"
    )
    {
        var tool = new ScriptedTool(
            name,
            "Spawns a subagent.",
            """{"type":"object","properties":{"prompt":{"type":"string"}}}""",
            ToolRisk.Write
        );
        tool.OnExecuteAsync = async (_, ctx, ct) =>
        {
            SubagentResult result = await run(ctx, ct);
            return new ToolResult(result.Output, result.IsError);
        };
        return tool;
    }

    private static ScriptedTool SpawnTool() =>
        SpawnTool((ctx, ct) => ctx.Subagents!.RunAsync(new SubagentRequest("child prompt"), ct));

    [Fact]
    public async Task A_child_run_is_linked_forwarded_and_accounted()
    {
        using var temp = new TempDirectory();
        string sessionPath = temp.File("sessions/parent.jsonl");
        Session session = Session.Create(sessionPath, temp.Root);
        ScriptedTool spawn = SpawnTool();
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Text("child answer"),
                LoopScripts.Usage(input: 10, output: 5),
                LoopScripts.Stop()
            )
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(spawn),
            new AgentHarnessOptions { Session = session }
        );

        List<AgentEvent> events = await Run(harness);

        RunStarted parentStart = events.OfType<RunStarted>().First();
        List<AgentEvent> childEvents =
        [
            .. events.Where(agentEvent => agentEvent.ParentRunId == parentStart.RunId),
        ];
        Assert.NotEmpty(childEvents);
        string childRunId = Assert.Single(childEvents.OfType<RunStarted>()).RunId;
        Assert.All(childEvents, childEvent => Assert.Equal(childRunId, childEvent.RunId));
        Assert.Equal("child answer", events.OfType<ToolCallResult>().Single().Output);
        RunFinished parentFinish = Assert.IsType<RunFinished>(events[^1]);
        Assert.Equal(StopReasons.Stop, parentFinish.StopReason);

        SessionChildSessionEntry link = Assert.Single(
            session.Entries.OfType<SessionChildSessionEntry>()
        );
        Assert.Equal(childRunId, link.RunId);
        string childPath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(sessionPath))!,
            SessionPaths.SessionFileName(link.ChildSessionId)
        );
        Assert.True(File.Exists(childPath), $"child session '{childPath}' should exist");
        Session childSession = Session.Load(childPath);
        Assert.Equal(temp.Root, childSession.Header.Cwd);
        Assert.Contains(
            childSession.Entries.OfType<SessionMessageEntry>(),
            entry => entry.Message.Role == ChatRole.User && entry.Message.Text == "child prompt"
        );
        Assert.All(
            childEvents,
            childEvent => Assert.Equal(link.ChildSessionId, childEvent.SessionId)
        );
        Assert.Equal(15, harness.SubagentUsage.TokensUsed);
        Assert.Equal(0, harness.SubagentUsage.LiveChildren);
    }

    [Fact]
    public async Task The_allow_list_narrows_the_child_registry_in_parent_order()
    {
        ScriptedTool read = ReadTool("contents");
        ScriptedTool spawn = SpawnTool(
            (ctx, ct) =>
                ctx.Subagents!.RunAsync(
                    new SubagentRequest("child prompt", AllowedTools: ["read"]),
                    ct
                )
        );
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("child answer"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(read, spawn), new AgentHarnessOptions());

        await Run(harness);

        Assert.Equal(3, client.Requests.Count);
        Assert.Equal(["read"], client.Requests[1].Options!.Tools!.Select(tool => tool.Name));
    }

    [Fact]
    public async Task An_unknown_allow_list_name_refuses_the_spawn()
    {
        SubagentResult? captured = null;
        ScriptedTool spawn = SpawnTool(
            async (ctx, ct) =>
                captured = await ctx.Subagents!.RunAsync(
                    new SubagentRequest("child prompt", AllowedTools: ["nosuch"]),
                    ct
                )
        );
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(spawn), new AgentHarnessOptions());

        await Run(harness);

        Assert.NotNull(captured);
        Assert.True(captured.IsError);
        Assert.Contains("nosuch", captured.Output, StringComparison.Ordinal);
        Assert.Null(captured.ChildSessionId);
        Assert.Null(captured.RunId);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task A_depth_one_runner_refuses_every_spawn()
    {
        SubagentResult? captured = null;
        ScriptedTool spawn = SpawnTool(
            async (ctx, ct) =>
                captured = await ctx.Subagents!.RunAsync(new SubagentRequest("child"), ct)
        );
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(spawn), new AgentHarnessOptions())
        {
            SpawnDepth = 1,
        };

        List<AgentEvent> events = await Run(harness);

        Assert.NotNull(captured);
        Assert.Equal(StopReasons.DepthLimit, captured.StopReason);
        Assert.Contains("depth_limit", captured.Output, StringComparison.Ordinal);
        Assert.Contains("subagents.depth", captured.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(events, agentEvent => agentEvent.ParentRunId is not null);
    }

    [Fact]
    public async Task Cancelling_the_call_cancels_the_child_and_joins()
    {
        SubagentResult? captured = null;
        ScriptedTool spawn = SpawnTool(
            async (ctx, ct) =>
                captured = await ctx.Subagents!.RunAsync(new SubagentRequest("child prompt"), ct)
        );
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("never reached"), LoopScripts.Stop());
        client.Gate(2);
        using var temp = new TempDirectory();
        Session session = Session.Create(temp.File("sessions/parent.jsonl"), temp.Root);
        var harness = new AgentHarness(
            client,
            Registry(spawn),
            new AgentHarnessOptions { Session = session }
        );
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        List<AgentEvent>? events = null;
        Task run = Task.Run(
            async () =>
            {
                events = await harness
                    .RunAsync("go", cancellation.Token)
                    .ToListAsync(CancellationToken.None);
            },
            CancellationToken.None
        );

        await client.WaitForCallAsync(2);
        // The link is appended before the child starts: it exists while the child is still gated.
        Assert.Single(session.Entries.OfType<SessionChildSessionEntry>());
        cancellation.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.NotNull(captured);
        Assert.Equal(StopReasons.Cancelled, captured.StopReason);
        Assert.True(captured.IsError);
        Assert.NotNull(events);
        Assert.Contains(events.OfType<RunFinished>(), finished => finished.ParentRunId is not null);
    }

    [Fact]
    public async Task Hook_payloads_during_a_child_run_carry_both_run_ids()
    {
        ScriptedTool read = ReadTool("contents");
        ScriptedTool spawn = SpawnTool();
        var hooks = new RecordingHookPoints();
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("child done"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(read, spawn),
            new AgentHarnessOptions { Hooks = hooks }
        );

        List<AgentEvent> events = await Run(harness);

        string parentRunId = events.OfType<RunStarted>().First().RunId;
        string childRunId = events
            .OfType<RunStarted>()
            .Single(start => start.RunId != parentRunId)
            .RunId;
        AgentToolCallingContext childCalling = hooks.ToolCallings.Single(context =>
            context.RunId == childRunId
        );
        Assert.Equal(parentRunId, childCalling.ParentRunId);
        Assert.All(
            hooks.ToolCallings.Where(context => context.RunId == parentRunId),
            context => Assert.Null(context.ParentRunId)
        );
        Assert.Contains(
            hooks.RunStartings,
            context => context.RunId == childRunId && context.ParentRunId == parentRunId
        );
    }

    [Fact]
    public async Task A_run_without_a_session_still_spawns_and_reports_no_child_session()
    {
        SubagentResult? captured = null;
        ScriptedTool spawn = SpawnTool(
            async (ctx, ct) =>
                captured = await ctx.Subagents!.RunAsync(new SubagentRequest("child prompt"), ct)
        );
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("child answer"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(spawn), new AgentHarnessOptions());

        await Run(harness);

        Assert.NotNull(captured);
        Assert.Null(captured.ChildSessionId);
        Assert.NotNull(captured.RunId);
        Assert.Equal("child answer", captured.Output);
        Assert.Equal(StopReasons.Stop, captured.StopReason);
        Assert.False(captured.IsError);
    }

    [Fact]
    public async Task Tool_context_subagents_is_stamped_in_a_run_and_null_outside()
    {
        SubagentToolProbe probe = new();
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "probe", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(probe), new AgentHarnessOptions());

        await Run(harness);

        Assert.NotNull(probe.InRunRunner);
        Assert.Equal(0, probe.InRunRunner.Depth);

        ToolResult outside = await probe.ExecuteAsync(
            default,
            new ToolContext(Environment.CurrentDirectory, new RecordingAgentEvents()),
            Ct
        );
        Assert.Equal("null", outside.Output);
    }

    [Fact]
    public async Task Calls_of_one_run_share_the_runs_runner()
    {
        var runners = new List<ISubagentRunner>();
        ScriptedTool first = SpawnTool(
            (ctx, _) =>
            {
                runners.Add(ctx.Subagents!);
                return Task.FromResult(Refusal("first"));
            }
        );
        ScriptedTool second = SpawnTool(
            (ctx, _) =>
            {
                runners.Add(ctx.Subagents!);
                return Task.FromResult(Refusal("second"));
            },
            name: "spawn2"
        );
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Calls(
                    new FunctionCallContent("call_1", "spawn", LoopScripts.Args()),
                    new FunctionCallContent("call_2", "spawn2", LoopScripts.Args())
                ),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(first, second), new AgentHarnessOptions());

        await Run(harness);

        Assert.Equal(2, runners.Count);
        Assert.Same(runners[0], runners[1]);
        Assert.Equal(0, runners[0].Depth);
    }

    private static SubagentResult Refusal(string output) =>
        new(null, null, output, StopReasons.Error, IsError: true, TokensUsed: 0);

    private sealed class SubagentToolProbe : ITool
    {
        public ISubagentRunner? InRunRunner { get; private set; }

        public string Name => "probe";

        public string Description => "Probes the subagent seam.";

        public System.Text.Json.JsonElement ParametersSchema { get; } =
            System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

        public Task<ToolResult> ExecuteAsync(
            System.Text.Json.JsonElement args,
            ToolContext ctx,
            CancellationToken ct
        )
        {
            InRunRunner ??= ctx.Subagents;
            return Task.FromResult(
                new ToolResult(ctx.Subagents is null ? "null" : "runner", IsError: false)
            );
        }
    }
}
