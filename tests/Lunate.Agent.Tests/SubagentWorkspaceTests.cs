using System.Text.Json;
using Microsoft.Extensions.AI;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

/// <summary>
/// The child-run workspace surface: root resolution and its refusals, the workspace-tool factory,
/// the child's own session header and the workspace root on approval asks.
/// </summary>
public sealed class SubagentWorkspaceTests
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
    public async Task A_workspace_child_runs_in_its_own_root_and_records_its_own_header()
    {
        using var temp = new TempDirectory();
        var (main, worktree) = CreateLinkedWorktree(temp, "wt");
        string canonicalMain = GitIdentity.Canonicalize(main);
        string canonicalWorktree = GitIdentity.Canonicalize(worktree);
        Session parentSession = Session.Create(
            temp.File("sessions/parent.jsonl"),
            canonicalMain,
            GitIdentity.Canonicalize(Path.Combine(main, ".git")),
            canonicalMain
        );
        var probe = new WorkspaceWriteProbe();
        var factoryRoots = new List<string>();
        var approver = new RecordingApprover(decision: true);
        ScriptedTool spawn = SpawnTool(
            (ctx, ct) =>
                ctx.Subagents!.RunAsync(
                    new SubagentRequest("child prompt", WorkspaceRoot: worktree),
                    ct
                )
        );
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("call_1", "child-probe", LoopScripts.Args(("path", "child.txt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("child done"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(spawn),
            new AgentHarnessOptions
            {
                WorkingDirectory = canonicalMain,
                Session = parentSession,
                Approver = approver,
                WorkspaceTools = root =>
                {
                    factoryRoots.Add(root);
                    return Registry(probe);
                },
            }
        );

        List<AgentEvent> events = await Run(harness);

        // The factory built the child's set for the canonical child root and the child's tools ran
        // there: its relative write landed in the worktree, not in the main checkout.
        Assert.Equal(new[] { canonicalWorktree }, factoryRoots);
        Assert.Equal(canonicalWorktree, probe.ContextRoot);
        Assert.True(File.Exists(Path.Combine(worktree, "child.txt")));
        Assert.False(File.Exists(Path.Combine(main, "child.txt")));

        // The child session sits beside the parent's and records the child's own repo/worktree.
        SessionChildSessionEntry link = Assert.Single(
            parentSession.Entries.OfType<SessionChildSessionEntry>()
        );
        string childPath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(parentSession.Path))!,
            SessionPaths.SessionFileName(link.ChildSessionId)
        );
        Session childSession = Session.Load(childPath);
        Assert.Equal(canonicalWorktree, childSession.Header.Cwd);
        Assert.Equal(canonicalWorktree, childSession.Header.Worktree);
        Assert.Equal(
            GitIdentity.Canonicalize(Path.Combine(main, ".git")),
            childSession.Header.Repo
        );
        Assert.Equal(canonicalMain, parentSession.Header.Worktree);

        // The ask carries the asking run's workspace root: the child's. The parent's own ask keeps
        // the parent's root.
        Assert.Contains(
            approver.Requests,
            request =>
                request.Tool.Name == "child-probe" && request.WorkspaceRoot == canonicalWorktree
        );
        Assert.Contains(
            approver.Requests,
            request => request.Tool.Name == "spawn" && request.WorkspaceRoot == canonicalMain
        );
        Assert.Contains(events.OfType<RunStarted>(), start => start.ParentRunId is not null);
    }

    [Fact]
    public async Task The_child_never_sees_the_parents_tool_instances()
    {
        using var temp = new TempDirectory();
        var (main, worktree) = CreateLinkedWorktree(temp, "wt");
        string canonicalMain = GitIdentity.Canonicalize(main);
        ScriptedTool parentWrite = new(
            "parent-write",
            "A parent-only write tool.",
            """{"type":"object"}""",
            ToolRisk.Write
        );
        var probe = new WorkspaceWriteProbe();
        ScriptedTool spawn = SpawnTool(
            (ctx, ct) =>
                ctx.Subagents!.RunAsync(
                    new SubagentRequest("child prompt", WorkspaceRoot: worktree),
                    ct
                )
        );
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("call_1", "parent-write", LoopScripts.Args()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("child done"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(parentWrite, spawn),
            new AgentHarnessOptions
            {
                WorkingDirectory = canonicalMain,
                WorkspaceTools = _ => Registry(probe),
            }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.Null(parentWrite.ReceivedContext);
        Assert.Contains(
            events.OfType<ToolCallResult>(),
            result =>
                result.IsError
                && result.RunId != events.OfType<RunStarted>().First().RunId
                && result.Output.Contains("Unknown tool", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task The_allow_list_applies_to_the_childs_own_set()
    {
        using var temp = new TempDirectory();
        var (main, worktree) = CreateLinkedWorktree(temp, "wt");
        string canonicalMain = GitIdentity.Canonicalize(main);
        ScriptedTool read = ReadTool("contents");
        var probe = new WorkspaceWriteProbe();
        ScriptedTool spawn = SpawnTool(
            (ctx, ct) =>
                ctx.Subagents!.RunAsync(
                    new SubagentRequest(
                        "child prompt",
                        AllowedTools: ["child-probe"],
                        WorkspaceRoot: worktree
                    ),
                    ct
                )
        );
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("child done"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(read, spawn),
            new AgentHarnessOptions
            {
                WorkingDirectory = canonicalMain,
                WorkspaceTools = _ => Registry(probe),
            }
        );

        await Run(harness);

        Assert.Equal(["child-probe"], client.Requests[1].Options!.Tools!.Select(tool => tool.Name));
    }

    [Fact]
    public async Task An_allow_list_name_only_the_parent_offers_refuses()
    {
        using var temp = new TempDirectory();
        var (main, worktree) = CreateLinkedWorktree(temp, "wt");
        string canonicalMain = GitIdentity.Canonicalize(main);
        ScriptedTool read = ReadTool("contents");
        var probe = new WorkspaceWriteProbe();
        SubagentResult? captured = null;
        ScriptedTool spawn = SpawnTool(
            async (ctx, ct) =>
                captured = await ctx.Subagents!.RunAsync(
                    new SubagentRequest(
                        "child prompt",
                        AllowedTools: ["read"],
                        WorkspaceRoot: worktree
                    ),
                    ct
                )
        );
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(read, spawn),
            new AgentHarnessOptions
            {
                WorkingDirectory = canonicalMain,
                WorkspaceTools = _ => Registry(probe),
            }
        );

        await Run(harness);

        Assert.NotNull(captured);
        Assert.True(captured.IsError);
        Assert.Contains("read", captured.Output, StringComparison.Ordinal);
        Assert.Null(captured.RunId);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task A_missing_or_nondirectory_root_refuses_naming_the_path()
    {
        using var temp = new TempDirectory();
        var (main, _) = CreateLinkedWorktree(temp, "wt");
        string canonicalMain = GitIdentity.Canonicalize(main);
        string missing = temp.File("missing-worktree");
        File.WriteAllText(temp.File("plain-file"), "x");

        foreach (string root in new[] { missing, temp.File("plain-file") })
        {
            SubagentResult? captured = null;
            ScriptedTool spawn = SpawnTool(
                async (ctx, ct) =>
                    captured = await ctx.Subagents!.RunAsync(
                        new SubagentRequest("child prompt", WorkspaceRoot: root),
                        ct
                    )
            );
            var client = new ScriptedChatClient()
                .Enqueue(
                    LoopScripts.Call(
                        "call_1",
                        "spawn",
                        LoopScripts.Args(("prompt", "child prompt"))
                    ),
                    LoopScripts.ToolCalls()
                )
                .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
            var harness = new AgentHarness(
                client,
                Registry(spawn),
                new AgentHarnessOptions
                {
                    WorkingDirectory = canonicalMain,
                    WorkspaceTools = _ => Registry(new WorkspaceWriteProbe()),
                }
            );

            List<AgentEvent> events = await Run(harness);

            Assert.NotNull(captured);
            Assert.Equal(StopReasons.WorkspaceMissing, captured.StopReason);
            Assert.True(captured.IsError);
            Assert.Contains(root, captured.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(events, agentEvent => agentEvent.ParentRunId is not null);
        }
    }

    [Fact]
    public async Task A_root_outside_the_repository_refuses()
    {
        using var temp = new TempDirectory();
        var (main, _) = CreateLinkedWorktree(temp, "wt");
        string canonicalMain = GitIdentity.Canonicalize(main);
        string otherRepo = temp.File("other-repo");
        Directory.CreateDirectory(Path.Combine(otherRepo, ".git"));
        SubagentResult? captured = null;
        ScriptedTool spawn = SpawnTool(
            async (ctx, ct) =>
                captured = await ctx.Subagents!.RunAsync(
                    new SubagentRequest("child prompt", WorkspaceRoot: otherRepo),
                    ct
                )
        );
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(spawn),
            new AgentHarnessOptions
            {
                WorkingDirectory = canonicalMain,
                WorkspaceTools = _ => Registry(new WorkspaceWriteProbe()),
            }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.NotNull(captured);
        Assert.Equal(StopReasons.WorkspaceOutsideRepository, captured.StopReason);
        Assert.Contains("git-common-dir", captured.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(events, agentEvent => agentEvent.ParentRunId is not null);
    }

    [Fact]
    public async Task A_workspace_spawn_without_a_tool_factory_refuses()
    {
        using var temp = new TempDirectory();
        var (main, worktree) = CreateLinkedWorktree(temp, "wt");
        string canonicalMain = GitIdentity.Canonicalize(main);
        SubagentResult? captured = null;
        ScriptedTool spawn = SpawnTool(
            async (ctx, ct) =>
                captured = await ctx.Subagents!.RunAsync(
                    new SubagentRequest("child prompt", WorkspaceRoot: worktree),
                    ct
                )
        );
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(spawn),
            new AgentHarnessOptions { WorkingDirectory = canonicalMain }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.NotNull(captured);
        Assert.Equal(StopReasons.WorkspaceToolsUnavailable, captured.StopReason);
        Assert.Contains("WorkspaceTools", captured.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(events, agentEvent => agentEvent.ParentRunId is not null);
    }

    [Fact]
    public async Task Naming_the_parents_own_root_behaves_as_inherit()
    {
        using var temp = new TempDirectory();
        var (main, _) = CreateLinkedWorktree(temp, "wt");
        string canonicalMain = GitIdentity.Canonicalize(main);
        Session parentSession = Session.Create(
            temp.File("sessions/parent.jsonl"),
            canonicalMain,
            GitIdentity.Canonicalize(Path.Combine(main, ".git")),
            canonicalMain
        );
        SubagentResult? captured = null;
        ScriptedTool spawn = SpawnTool(
            async (ctx, ct) =>
                captured = await ctx.Subagents!.RunAsync(
                    new SubagentRequest("child prompt", WorkspaceRoot: main),
                    ct
                )
        );
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "spawn", LoopScripts.Args(("prompt", "child prompt"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("child done"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(spawn),
            new AgentHarnessOptions { WorkingDirectory = canonicalMain, Session = parentSession }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.NotNull(captured);
        Assert.Equal(StopReasons.Stop, captured.StopReason);
        Assert.False(captured.IsError);
        Assert.Contains(events.OfType<RunStarted>(), start => start.ParentRunId is not null);
        SessionChildSessionEntry link = Assert.Single(
            parentSession.Entries.OfType<SessionChildSessionEntry>()
        );
        Session childSession = Session.Load(
            Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(parentSession.Path))!,
                SessionPaths.SessionFileName(link.ChildSessionId)
            )
        );
        Assert.Equal(canonicalMain, childSession.Header.Cwd);
        Assert.Equal(canonicalMain, childSession.Header.Worktree);
    }

    private static (string Main, string Worktree) CreateLinkedWorktree(
        TempDirectory temp,
        string name
    )
    {
        var main = temp.File("main");
        var worktree = temp.File(name);
        var gitDir = Path.Combine(main, ".git", "worktrees", name);
        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(worktree);
        File.WriteAllText(
            Path.Combine(worktree, ".git"),
            $"gitdir: ../main/.git/worktrees/{name}\n"
        );
        File.WriteAllText(Path.Combine(gitDir, "commondir"), "../..\n");
        return (main, worktree);
    }

    /// <summary>A write-risk probe that writes a file relative to the run's working directory.</summary>
    private sealed class WorkspaceWriteProbe : ITool
    {
        private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(
            new { type = "object", properties = new { path = new { type = "string" } } }
        );

        public string? ContextRoot { get; private set; }

        public string Name => "child-probe";

        public string Description => "Writes a file relative to the run's working directory.";

        public JsonElement ParametersSchema => Schema;

        public ToolRisk Risk => ToolRisk.Write;

        public Task<ToolResult> ExecuteAsync(
            JsonElement args,
            ToolContext ctx,
            CancellationToken ct
        )
        {
            ContextRoot = ctx.WorkingDirectory;
            string path = args.GetProperty("path").GetString()!;
            string target = Path.Combine(ctx.WorkingDirectory, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "child");
            return Task.FromResult(new ToolResult($"wrote {path}", IsError: false));
        }
    }
}
