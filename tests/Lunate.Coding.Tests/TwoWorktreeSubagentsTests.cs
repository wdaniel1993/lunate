using System.Text.Json;
using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// Two subagents in two worktrees of one repository: each child edits its own worktree, a write
/// across the boundary is refused, both child sessions sit beside the parent's with their own
/// headers and both event streams reach the parent stream.
/// </summary>
public sealed class TwoWorktreeSubagentsTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_children_edit_their_own_worktrees_and_a_cross_write_is_refused()
    {
        using var temp = new TempDirectory();
        var (main, worktreeA) = CreateLinkedWorktree(temp, "a");
        var (_, worktreeB) = CreateLinkedWorktree(temp, "b");
        string canonicalMain = GitIdentity.Canonicalize(main);
        string canonicalA = GitIdentity.Canonicalize(worktreeA);
        string canonicalB = GitIdentity.Canonicalize(worktreeB);
        using var services = new WorkspaceServices(2, root => new FakeWorkspaceBackend(root));
        Session session = Session.Create(temp.File("sessions/parent.jsonl"), canonicalMain);
        ToolRegistry parentTools = RoslynTestHarness.CreateTools(
            new Workspace(canonicalMain),
            services: services
        );
        parentTools.Add(new SpawnProbe("spawn-a", worktreeA));
        parentTools.Add(new SpawnProbe("spawn-b", worktreeB));
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call("call_1", "spawn-a", Scripts.Args(("prompt", "edit a"))),
                Scripts.ToolCalls()
            )
            .Enqueue(
                Scripts.Call(
                    "call_1",
                    "write",
                    Scripts.Args(("path", "own.txt"), ("content", "from a\n"))
                ),
                Scripts.ToolCalls()
            )
            .Enqueue(
                Scripts.Call(
                    "call_1",
                    "write",
                    Scripts.Args(
                        ("path", Path.Combine(worktreeB, "cross.txt")),
                        ("content", "cross\n")
                    )
                ),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("a done"), Scripts.Stop())
            .Enqueue(
                Scripts.Call("call_2", "spawn-b", Scripts.Args(("prompt", "edit b"))),
                Scripts.ToolCalls()
            )
            .Enqueue(
                Scripts.Call(
                    "call_1",
                    "write",
                    Scripts.Args(("path", "own.txt"), ("content", "from b\n"))
                ),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("b done"), Scripts.Stop())
            .Enqueue(Scripts.Text("parent done"), Scripts.Stop());
        var harness = new AgentHarness(
            client,
            parentTools,
            new AgentHarnessOptions
            {
                WorkingDirectory = canonicalMain,
                Session = session,
                WorkspaceTools = RoslynTestHarness.CreateWorkspaceTools(services, []),
            }
        );

        List<AgentEvent> events = await harness.RunAsync("go", Ct).ToListAsync(Ct);

        Assert.True(File.Exists(Path.Combine(worktreeA, "own.txt")));
        Assert.True(File.Exists(Path.Combine(worktreeB, "own.txt")));
        Assert.False(File.Exists(Path.Combine(worktreeA, "cross.txt")));
        Assert.False(File.Exists(Path.Combine(worktreeB, "cross.txt")));
        Assert.False(File.Exists(Path.Combine(main, "own.txt")));
        Assert.Contains(
            events.OfType<ToolCallResult>(),
            result =>
                result.IsError
                && result.ParentRunId is not null
                && result.Output.Contains("outside the workspace", StringComparison.Ordinal)
        );

        string parentRunId = events.OfType<RunStarted>().First().RunId;
        List<string> childRuns =
        [
            .. events
                .OfType<RunStarted>()
                .Where(start => start.ParentRunId == parentRunId)
                .Select(start => start.RunId),
        ];
        Assert.Equal(2, childRuns.Count);
        Assert.Equal(2, session.Entries.OfType<SessionChildSessionEntry>().Count());
        List<(string Worktree, string Cwd)> headers = [];
        foreach (
            SessionChildSessionEntry link in session.Entries.OfType<SessionChildSessionEntry>()
        )
        {
            string childPath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(session.Path))!,
                SessionPaths.SessionFileName(link.ChildSessionId)
            );
            string firstLine = File.ReadLines(childPath).First();
            using var document = JsonDocument.Parse(firstLine);
            headers.Add(
                (
                    document.RootElement.GetProperty("worktree").GetString()!,
                    document.RootElement.GetProperty("cwd").GetString()!
                )
            );
        }

        Assert.Equal([canonicalA, canonicalB], headers.Select(header => header.Worktree));
        Assert.All(headers, header => Assert.Equal(header.Worktree, header.Cwd));
        Assert.All(
            childRuns,
            runId => Assert.Contains(events, agentEvent => agentEvent.RunId == runId)
        );
    }

    private static (string Main, string Worktree) CreateLinkedWorktree(
        TempDirectory temp,
        string name
    )
    {
        var main = temp.File("main");
        var worktree = temp.File("wt-" + name);
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
}
