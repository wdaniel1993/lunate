using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

/// <summary>The core subagent limits: per-child and per-tree token budgets, concurrency and clamps.</summary>
public sealed class SubagentLimitsTests
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

    [Fact]
    public async Task A_child_stops_at_its_token_budget_before_the_next_model_call()
    {
        var captured = new List<SubagentResult>();
        ScriptedTool spawn = SpawnTool(
            async (ctx, ct) =>
            {
                SubagentResult result = await ctx.Subagents!.RunAsync(
                    new SubagentRequest("child prompt"),
                    ct
                );
                captured.Add(result);
                return result;
            }
        );
        ScriptedTool read = ReadTool("contents");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Text("first part"),
                LoopScripts.Usage(input: 6, output: 6),
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(read, spawn),
            new AgentHarnessOptions
            {
                SubagentLimits = new SubagentLimits(
                    Concurrency: 4,
                    TokenBudget: 10,
                    TreeTokenBudget: 1_000
                ),
            }
        );

        List<AgentEvent> events = await Run(harness);

        SubagentResult result = Assert.Single(captured);
        Assert.Equal(StopReasons.TokenLimit, result.StopReason);
        Assert.True(result.IsError);
        Assert.Equal("first part", result.Output);
        Assert.Equal(12, result.TokensUsed);
        // Pre-call timing: the child's second model call never happened (parent, child, parent).
        Assert.Equal(3, client.Requests.Count);
        Assert.Contains(
            events.OfType<RunFinished>(),
            finished =>
                finished.ParentRunId is not null && finished.StopReason == StopReasons.TokenLimit
        );
        Assert.Equal(StopReasons.Stop, Assert.IsType<RunFinished>(events[^1]).StopReason);
    }

    [Fact]
    public async Task An_exhausted_tree_budget_refuses_the_next_spawn()
    {
        var captured = new List<SubagentResult>();
        ScriptedTool spawn = SpawnTool(
            async (ctx, ct) =>
            {
                SubagentResult result = await ctx.Subagents!.RunAsync(
                    new SubagentRequest("child prompt"),
                    ct
                );
                captured.Add(result);
                return result;
            }
        );
        ScriptedTool read = ReadTool("contents");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Text("first part"),
                LoopScripts.Usage(input: 6, output: 6),
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("call_2", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(read, spawn),
            new AgentHarnessOptions
            {
                SubagentLimits = new SubagentLimits(
                    Concurrency: 4,
                    TokenBudget: 1_000,
                    TreeTokenBudget: 10
                ),
            }
        );

        await Run(harness);

        Assert.Equal(2, captured.Count);
        Assert.Equal(StopReasons.TokenLimit, captured[0].StopReason);
        Assert.Equal(StopReasons.TokenLimit, captured[1].StopReason);
        Assert.Null(captured[1].RunId);
        Assert.Contains("subagents.treeTokenBudget", captured[1].Output, StringComparison.Ordinal);
        Assert.Equal(12, harness.SubagentUsage.TokensUsed);
    }

    [Fact]
    public async Task A_spawn_while_the_tree_concurrency_is_held_is_refused_immediately()
    {
        var captured = new List<SubagentResult>();
        ScriptedChatClient? client = null;
        ScriptedTool spawn = SpawnTool(
            async (ctx, ct) =>
            {
                ISubagentRunner runner = ctx.Subagents!;
                Task<SubagentResult> firstTask = runner.RunAsync(new SubagentRequest("one"), ct);
                await client!.WaitForCallAsync(2);
                SubagentResult second = await runner.RunAsync(new SubagentRequest("two"), ct);
                client.Release(2);
                SubagentResult first = await firstTask;
                captured.Add(first);
                captured.Add(second);
                return second;
            }
        );
        client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "one"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("one done"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        client.Gate(2);
        var harness = new AgentHarness(
            client,
            Registry(spawn),
            new AgentHarnessOptions { SubagentLimits = new SubagentLimits(Concurrency: 1) }
        );

        await Run(harness);

        Assert.Equal(2, captured.Count);
        Assert.Equal(StopReasons.Stop, captured[0].StopReason);
        Assert.Equal(StopReasons.ConcurrencyLimit, captured[1].StopReason);
        Assert.Null(captured[1].RunId);
        Assert.Contains("subagents.concurrency", captured[1].Output, StringComparison.Ordinal);
        // The refusal never reached the model; only the held child and the parent ran.
        Assert.DoesNotContain(
            client.Requests,
            request =>
                request.Messages.Any(message =>
                    message.Text?.Contains("two", StringComparison.Ordinal) == true
                )
        );
    }

    [Fact]
    public async Task A_per_spawn_step_request_clamps_to_the_resolved_ceiling()
    {
        var captured = new List<SubagentResult>();
        ScriptedTool spawn = SpawnTool(
            async (ctx, ct) =>
            {
                SubagentResult result = await ctx.Subagents!.RunAsync(
                    new SubagentRequest("child prompt", MaxSteps: 100),
                    ct
                );
                captured.Add(result);
                return result;
            }
        );
        ScriptedTool read = ReadTool("contents");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("call_2", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(read, spawn),
            new AgentHarnessOptions { MaxSteps = 2 }
        );

        await Run(harness);

        SubagentResult result = Assert.Single(captured);
        Assert.Equal(StopReasons.StepLimit, result.StopReason);
        // Parent call, the two clamped child calls, then the parent's answer.
        Assert.Equal(4, client.Requests.Count);
    }

    [Fact]
    public async Task The_loop_stops_at_the_run_token_budget_with_the_partial_output()
    {
        ScriptedTool read = ReadTool("contents");
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Text("partial answer"),
                LoopScripts.Usage(input: 6, output: 6),
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("never reached"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(read),
            new AgentHarnessOptions { TokenBudget = 10, MaxSteps = 5 }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.Single(client.Requests);
        Assert.Equal(
            "partial answer",
            string.Concat(events.OfType<TextMessageContent>().Select(content => content.Text))
        );
        RunFinished finished = Assert.IsType<RunFinished>(events[^1]);
        Assert.Equal(StopReasons.TokenLimit, finished.StopReason);
    }

    [Fact]
    public async Task Concurrent_children_share_the_live_tree_budget_not_a_frozen_copy()
    {
        var captured = new List<SubagentResult>();
        ScriptedChatClient? client = null;
        ScriptedTool spawn = SpawnTool(
            async (ctx, ct) =>
            {
                ISubagentRunner runner = ctx.Subagents!;
                Task<SubagentResult> firstTask = runner.RunAsync(new SubagentRequest("one"), ct);
                await client!.WaitForCallAsync(2);
                Task<SubagentResult> secondTask = runner.RunAsync(new SubagentRequest("two"), ct);
                await client.WaitForCallAsync(3);
                client.Release(2);
                // A's first call completed: it passes its own pre-call check (the tree is not yet
                // over) and issues its second call — only then is B's first call released.
                await client.WaitForCallAsync(4);
                client.Release(3);
                SubagentResult first = await firstTask;
                SubagentResult second = await secondTask;
                captured.Add(first);
                captured.Add(second);
                return second;
            }
        );
        ScriptedTool read = ReadTool("contents");
        // Both children spawn while the tree is still untouched, then run concurrently: an
        // implementation that freezes the tree's remaining budget at spawn would let both pass
        // their own frozen ceilings; the live check stops them instead.
        client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "one"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Text("A part"),
                LoopScripts.Usage(input: 30, output: 30),
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Text("B part"),
                LoopScripts.Usage(input: 30, output: 30),
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Usage(input: 30, output: 30),
                LoopScripts.Call("call_1", "read", LoopScripts.Args(("path", "a.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        client.Gate(2);
        client.Gate(3);
        var harness = new AgentHarness(
            client,
            Registry(read, spawn),
            new AgentHarnessOptions
            {
                SubagentLimits = new SubagentLimits(
                    Concurrency: 4,
                    TokenBudget: 1_000,
                    TreeTokenBudget: 100
                ),
            }
        );

        await Run(harness);

        Assert.Equal(2, captured.Count);
        SubagentResult first = captured[0];
        SubagentResult second = captured[1];
        Assert.Equal(StopReasons.TokenLimit, first.StopReason);
        Assert.Equal(StopReasons.TokenLimit, second.StopReason);
        Assert.True(first.IsError);
        Assert.True(second.IsError);
        // The second child stopped right at its first call (tree already over); the first child's
        // second call ran, and the live check stopped it before its third.
        Assert.Equal(60, second.TokensUsed);
        Assert.Equal(120, first.TokensUsed);
        Assert.Equal(180, harness.SubagentUsage.TokensUsed);
        Assert.Equal(5, client.Requests.Count);
    }

    [Fact]
    public void Invalid_subagent_limits_fail_fast()
    {
        var client = new ScriptedChatClient();

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentHarness(
                client,
                new ToolRegistry(),
                new AgentHarnessOptions { SubagentLimits = new SubagentLimits(Concurrency: 0) }
            )
        );

        Assert.Equal(nameof(AgentHarnessOptions.SubagentLimits), exception.ParamName);
    }
}
