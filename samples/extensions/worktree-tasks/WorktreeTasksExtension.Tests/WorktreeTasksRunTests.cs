using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Testing;
using Microsoft.Extensions.AI;

namespace WorktreeTasksExtension.Tests;

/// <summary>
/// The done-when against real temp git repositories: start runs a child in its new worktree, merge
/// brings the work back (and aborts conflicts), clean removes worktree and branch, list reports
/// them, and bad names or existing branches are refused.
/// </summary>
public sealed class WorktreeTasksRunTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static string LastToolResult(ScriptedModelClient client, int requestIndex) =>
        Assert
            .Single(client.Requests[requestIndex][^1].Contents.OfType<FunctionResultContent>())
            .Result!.ToString()!;

    [Fact]
    public async Task Start_creates_the_worktree_and_runs_the_child_in_it()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp);
        await host.StartAsync(Ct);
        await TestSupport.InitRepositoryAsync(host, "base.txt");
        host.ComposeTools();
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "worktree__start",
                    ModelScripts.Args(("name", "wt-a"), ("task", "write child.txt"))
                )
            )
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "write",
                    ModelScripts.Args(("path", "child.txt"), ("content", "hello\n"))
                )
            )
            .Enqueue(ModelScripts.Text("wrote the file"))
            .Enqueue(ModelScripts.Text("parent done"));

        ExtensionRunResult result = await host.RunAsync(client, "go", Ct);

        string worktree = TestSupport.WorktreePath(host, "wt-a");
        Assert.True(Directory.Exists(worktree), "the worktree directory should exist");
        Assert.True(File.Exists(Path.Combine(worktree, "child.txt")));
        Assert.False(File.Exists(Path.Combine(host.WorkingDirectory, "child.txt")));
        (int code, string branches) = await TestSupport.GitAsync(
            host,
            TestSupport.ProcessRunner(host),
            "branch",
            "--list",
            "lunate/wt-a"
        );
        Assert.Equal(0, code);
        Assert.Contains("lunate/wt-a", branches, StringComparison.Ordinal);
        string startResult = LastToolResult(client, 3);
        Assert.Contains("wrote the file", startResult, StringComparison.Ordinal);
        Assert.Contains("worktree: " + worktree, startResult, StringComparison.Ordinal);
        Assert.Contains("branch: lunate/wt-a", startResult, StringComparison.Ordinal);

        Session parent = Session.Load(result.SessionPath);
        SessionChildSessionEntry link = Assert.Single(
            parent.Entries.OfType<SessionChildSessionEntry>()
        );
        string childPath = TestSupport.ChildSessionPath(result.SessionPath, link.ChildSessionId);
        string canonicalWorktree = GitIdentity.Canonicalize(worktree);
        (string? repo, string? childWorktree, string? childCwd) = TestSupport.ReadHeader(childPath);
        Assert.Equal(canonicalWorktree, childCwd);
        Assert.Equal(canonicalWorktree, childWorktree);
        Assert.False(string.IsNullOrEmpty(repo));
    }

    [Fact]
    public async Task Merge_brings_the_worktree_commit_back_as_a_merge_commit()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp);
        await host.StartAsync(Ct);
        await TestSupport.InitRepositoryAsync(host, "base.txt");
        host.ComposeTools();
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "worktree__start",
                    ModelScripts.Args(("name", "wt-a"), ("task", "commit child.txt"))
                )
            )
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "write",
                    ModelScripts.Args(("path", "child.txt"), ("content", "hello\n"))
                )
            )
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "bash",
                    ModelScripts.Args(("command", "git add . && git commit -q -m child"))
                )
            )
            .Enqueue(ModelScripts.Text("committed"))
            .Enqueue(
                ModelScripts.Call("call_1", "worktree__merge", ModelScripts.Args(("name", "wt-a")))
            )
            .Enqueue(ModelScripts.Text("parent done"));

        await host.RunAsync(client, "go", Ct);

        Assert.True(File.Exists(Path.Combine(host.WorkingDirectory, "child.txt")));
        (int code, string log) = await TestSupport.GitAsync(
            host,
            TestSupport.ProcessRunner(host),
            "log",
            "--oneline",
            "--merges"
        );
        Assert.Equal(0, code);
        Assert.Contains("Merge branch 'lunate/wt-a'", log, StringComparison.Ordinal);
        string mergeResult = LastToolResult(client, 5);
        Assert.Contains("merged lunate/wt-a", mergeResult, StringComparison.Ordinal);
        Assert.DoesNotContain("failed", mergeResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_dirty_target_worktree_refuses_the_merge_naming_the_changes()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp);
        await host.StartAsync(Ct);
        await TestSupport.InitRepositoryAsync(host, "base.txt");
        host.ComposeTools();
        var startClient = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "worktree__start",
                    ModelScripts.Args(("name", "wt-a"), ("task", "nothing"))
                )
            )
            .Enqueue(ModelScripts.Text("child done"))
            .Enqueue(ModelScripts.Text("parent done"));
        await host.RunAsync(startClient, "start", Ct);

        File.WriteAllText(Path.Combine(host.WorkingDirectory, "uncommitted.txt"), "dirty\n");
        var mergeClient = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call("call_1", "worktree__merge", ModelScripts.Args(("name", "wt-a")))
            )
            .Enqueue(ModelScripts.Text("parent done"));

        await host.RunAsync(mergeClient, "merge", Ct);

        string refusal = LastToolResult(mergeClient, 1);
        Assert.Contains("uncommitted changes", refusal, StringComparison.Ordinal);
        Assert.Contains("uncommitted.txt", refusal, StringComparison.Ordinal);
        (int _, string log) = await TestSupport.GitAsync(
            host,
            TestSupport.ProcessRunner(host),
            "log",
            "--oneline",
            "--merges"
        );
        Assert.DoesNotContain("Merge branch", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_conflicting_merge_aborts_and_names_the_file()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp);
        await host.StartAsync(Ct);
        await TestSupport.InitRepositoryAsync(host, "base.txt");
        host.ComposeTools();
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "worktree__start",
                    ModelScripts.Args(("name", "wt-a"), ("task", "edit base"))
                )
            )
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "write",
                    ModelScripts.Args(("path", "base.txt"), ("content", "from a\n"))
                )
            )
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "bash",
                    ModelScripts.Args(("command", "git add . && git commit -q -m a"))
                )
            )
            .Enqueue(ModelScripts.Text("committed a"))
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "worktree__start",
                    ModelScripts.Args(("name", "wt-b"), ("task", "edit base"))
                )
            )
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "write",
                    ModelScripts.Args(("path", "base.txt"), ("content", "from b\n"))
                )
            )
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "bash",
                    ModelScripts.Args(("command", "git add . && git commit -q -m b"))
                )
            )
            .Enqueue(ModelScripts.Text("committed b"))
            .Enqueue(
                ModelScripts.Call("call_1", "worktree__merge", ModelScripts.Args(("name", "wt-a")))
            )
            .Enqueue(
                ModelScripts.Call("call_1", "worktree__merge", ModelScripts.Args(("name", "wt-b")))
            )
            .Enqueue(ModelScripts.Text("parent done"));

        await host.RunAsync(client, "go", Ct);

        string refusal = LastToolResult(client, client.Requests.Count - 1);
        Assert.Contains("merge", refusal, StringComparison.Ordinal);
        Assert.Contains("base.txt", refusal, StringComparison.Ordinal);
        Assert.Contains("aborted", refusal, StringComparison.Ordinal);
        (int statusCode, string status) = await TestSupport.GitAsync(
            host,
            TestSupport.ProcessRunner(host),
            "status",
            "--porcelain"
        );
        Assert.Equal(0, statusCode);
        Assert.Equal(string.Empty, status.Trim());
        (int headCode, _) = await TestSupport.GitAsync(
            host,
            TestSupport.ProcessRunner(host),
            "rev-parse",
            "--verify",
            "--quiet",
            "MERGE_HEAD"
        );
        Assert.NotEqual(0, headCode);
    }

    [Fact]
    public async Task Clean_removes_the_worktree_and_only_a_merged_branch()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp);
        await host.StartAsync(Ct);
        await TestSupport.InitRepositoryAsync(host, "base.txt");
        host.ComposeTools();
        var startClient = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "worktree__start",
                    ModelScripts.Args(("name", "wt-a"), ("task", "commit child.txt"))
                )
            )
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "write",
                    ModelScripts.Args(("path", "child.txt"), ("content", "hello\n"))
                )
            )
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "bash",
                    ModelScripts.Args(("command", "git add . && git commit -q -m child"))
                )
            )
            .Enqueue(ModelScripts.Text("committed"))
            .Enqueue(ModelScripts.Text("parent done"));
        await host.RunAsync(startClient, "start", Ct);
        string worktree = TestSupport.WorktreePath(host, "wt-a");
        Assert.True(Directory.Exists(worktree));

        // The unmerged branch refuses a clean without force.
        var refuseClient = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call("call_1", "worktree__clean", ModelScripts.Args(("name", "wt-a")))
            )
            .Enqueue(ModelScripts.Text("parent done"));
        await host.RunAsync(refuseClient, "clean", Ct);
        string refusal = LastToolResult(refuseClient, 1);
        Assert.Contains("not merged", refusal, StringComparison.Ordinal);
        Assert.True(Directory.Exists(worktree));

        // After the merge the clean removes the worktree and deletes the branch.
        var mergeClient = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call("call_1", "worktree__merge", ModelScripts.Args(("name", "wt-a")))
            )
            .Enqueue(ModelScripts.Text("parent done"));
        await host.RunAsync(mergeClient, "merge", Ct);
        var cleanClient = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call("call_1", "worktree__clean", ModelScripts.Args(("name", "wt-a")))
            )
            .Enqueue(ModelScripts.Text("parent done"));
        await host.RunAsync(cleanClient, "clean", Ct);

        Assert.False(Directory.Exists(worktree), "the worktree should be removed");
        (int code, string branches) = await TestSupport.GitAsync(
            host,
            TestSupport.ProcessRunner(host),
            "branch",
            "--list",
            "lunate/wt-a"
        );
        Assert.Equal(0, code);
        Assert.Equal(string.Empty, branches.Trim());
        string cleaned = LastToolResult(cleanClient, 1);
        Assert.Contains("removed worktree", cleaned, StringComparison.Ordinal);
        Assert.Contains("deleted branch 'lunate/wt-a'", cleaned, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clean_refuses_a_dirty_worktree_without_force()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp);
        await host.StartAsync(Ct);
        await TestSupport.InitRepositoryAsync(host, "base.txt");
        host.ComposeTools();
        var startClient = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "worktree__start",
                    ModelScripts.Args(("name", "wt-a"), ("task", "write but do not commit"))
                )
            )
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "write",
                    ModelScripts.Args(("path", "child.txt"), ("content", "hello\n"))
                )
            )
            .Enqueue(ModelScripts.Text("wrote"))
            .Enqueue(ModelScripts.Text("parent done"));
        await host.RunAsync(startClient, "start", Ct);
        string worktree = TestSupport.WorktreePath(host, "wt-a");

        var cleanClient = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call("call_1", "worktree__clean", ModelScripts.Args(("name", "wt-a")))
            )
            .Enqueue(ModelScripts.Text("parent done"));
        await host.RunAsync(cleanClient, "clean", Ct);

        Assert.True(Directory.Exists(worktree), "the dirty worktree must survive without force");
        Assert.Contains("failed", LastToolResult(cleanClient, 1), StringComparison.Ordinal);

        var forceClient = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "worktree__clean",
                    ModelScripts.Args(("name", "wt-a"), ("force", true))
                )
            )
            .Enqueue(ModelScripts.Text("parent done"));
        await host.RunAsync(forceClient, "clean", Ct);

        Assert.False(Directory.Exists(worktree), "force must remove the dirty worktree");
    }

    [Fact]
    public async Task Clean_refuses_while_a_run_is_active_in_the_worktree()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp);
        await host.StartAsync(Ct);
        await TestSupport.InitRepositoryAsync(host, "base.txt");
        host.ComposeTools();
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "worktree__start",
                    ModelScripts.Args(("name", "wt-a"), ("task", "hold"))
                )
            )
            .Enqueue(ModelScripts.Text("child done"))
            .Enqueue(ModelScripts.Text("parent done"));
        client.Gate(2);
        Task<ExtensionRunResult> run = host.RunAsync(client, "go", Ct);
        await client.WaitForCallAsync(2);

        ITool clean = host.Tools.Find("worktree__clean")!;
        ToolResult refusal = await clean.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { name = "wt-a" }),
            new ToolContext(host.WorkingDirectory, new NoopAgentEvents()),
            Ct
        );

        Assert.True(refusal.IsError);
        Assert.Contains("active", refusal.Output, StringComparison.Ordinal);
        Assert.True(Directory.Exists(TestSupport.WorktreePath(host, "wt-a")));
        client.Release(2);
        await run.WaitAsync(TimeSpan.FromSeconds(30), Ct);
    }

    [Fact]
    public async Task List_reports_both_worktrees_with_their_branches()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp);
        await host.StartAsync(Ct);
        await TestSupport.InitRepositoryAsync(host, "base.txt");
        host.ComposeTools();
        var client = new ScriptedModelClient()
            .Enqueue(
                ModelScripts.Call(
                    "call_1",
                    "worktree__start",
                    ModelScripts.Args(("name", "wt-a"), ("task", "nothing"))
                )
            )
            .Enqueue(ModelScripts.Text("child done"))
            .Enqueue(ModelScripts.Call("call_1", "worktree__list", ModelScripts.Args()))
            .Enqueue(ModelScripts.Text("parent done"));

        await host.RunAsync(client, "go", Ct);

        string list = LastToolResult(client, 3);
        Assert.Contains("2 worktrees:", list, StringComparison.Ordinal);
        Assert.Contains(host.WorkingDirectory, list, StringComparison.Ordinal);
        Assert.Contains("lunate/wt-a", list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bad_names_and_existing_branches_are_refused()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp);
        await host.StartAsync(Ct);
        await TestSupport.InitRepositoryAsync(host, "base.txt");
        host.ComposeTools();
        ITool start = host.Tools.Find("worktree__start")!;
        var context = new ToolContext(host.WorkingDirectory, new NoopAgentEvents())
        {
            Subagents = new StubSubagentRunner(),
        };

        foreach (string name in new[] { "../evil", "a/b", ".hidden", "name.lock" })
        {
            ToolResult refusal = await start.ExecuteAsync(
                JsonSerializer.SerializeToElement(new { name, task = "x" }),
                context,
                Ct
            );

            Assert.True(refusal.IsError, $"name '{name}' must be refused");
            Assert.Contains("not allowed", refusal.Output, StringComparison.Ordinal);
        }

        await TestSupport.GitAsync(host, TestSupport.ProcessRunner(host), "branch", "lunate/taken");
        ToolResult taken = await start.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { name = "taken", task = "x" }),
            context,
            Ct
        );

        Assert.True(taken.IsError);
        Assert.Contains("already exists", taken.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(TestSupport.WorktreePath(host, "taken")));
    }
}
