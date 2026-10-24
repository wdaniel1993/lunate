using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The bash tool's change report: after a command ran, the git-status diffed scan publishes what
/// it changed through the run's sink, tagged source <c>bash</c>; non-git workspaces emit nothing
/// and a cancelled call is still scanned.
/// </summary>
public sealed class BashToolChangeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Bash_calls_publish_changed_files_with_the_bash_source()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        var sink = new RecordingChangeSink();
        var context = new ToolContext(repo.Root, new NullAgentEvents())
        {
            FileChanges = sink,
            RunId = "run_1",
        };
        var tool = new BashTool(new Workspace(repo.Root));

        // The first call of a run only baselines; the edit call then reports the change.
        await tool.ExecuteAsync(CommandArgs("echo warm"), context, Ct);
        sink.Clear();
        ToolResult result = await tool.ExecuteAsync(
            CommandArgs("printf 'class A { int x; }\\n' > a.cs"),
            context,
            Ct
        );

        Assert.False(result.IsError, result.Output);
        FileChangeEvent change = Assert.Single(sink.Events);
        Assert.Equal("a.cs", change.RelativePath);
        Assert.Equal(FileChangeKind.Modified, change.Kind);
        Assert.Equal("bash", change.Source);
        Assert.Equal(Workspace.Canonicalize(Path.Combine(repo.Root, "a.cs")), change.AbsolutePath);
        Assert.Equal(repo.Root, change.WorkspaceId);
        Assert.Equal("run_1", change.RunId);
    }

    [Fact]
    public async Task A_non_git_workspace_emits_nothing()
    {
        using var temp = new TempDirectory();
        var sink = new RecordingChangeSink();
        var context = new ToolContext(temp.Root, new NullAgentEvents()) { FileChanges = sink };
        var tool = new BashTool(new Workspace(temp.Root));

        ToolResult result = await tool.ExecuteAsync(CommandArgs("echo hi"), context, Ct);

        Assert.False(result.IsError, result.Output);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task A_cancelled_call_is_still_scanned()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        var sink = new RecordingChangeSink();
        var context = new ToolContext(repo.Root, new NullAgentEvents())
        {
            FileChanges = sink,
            RunId = "run_1",
        };
        var tool = new BashTool(new Workspace(repo.Root));
        await tool.ExecuteAsync(CommandArgs("echo warm"), context, Ct);
        sink.Clear();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        Task<ToolResult> running = tool.ExecuteAsync(
            CommandArgs("printf 'class A { int x; }\\n' > a.cs && sleep 30"),
            context,
            cts.Token
        );
        // The shell redirect holds the file open while it sleeps; on Windows a plain
        // File.ReadAllText does not admit that writer and throws instead of polling. A locked file
        // simply means "not written yet" for this condition.
        await WaitUntilAsync(() =>
            ReadShared(Path.Combine(repo.Root, "a.cs"))?.Contains("int x", StringComparison.Ordinal)
            == true
        );
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        FileChangeEvent change = Assert.Single(sink.Events);
        Assert.Equal("a.cs", change.RelativePath);
        Assert.Equal("bash", change.Source);
    }

    /// <summary>Reads a file a live process may be writing, or null while it cannot be read yet.</summary>
    private static string? ReadShared(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50, Ct);
        }

        Assert.Fail("condition not reached within 30s");
    }

    private static JsonElement CommandArgs(string command) =>
        JsonSerializer.SerializeToElement(new { command });

    private sealed class RecordingChangeSink : IFileChangeSink
    {
        private readonly object _gate = new();
        private readonly List<FileChangeEvent> _events = [];

        public IReadOnlyList<FileChangeEvent> Events
        {
            get
            {
                lock (_gate)
                {
                    return [.. _events];
                }
            }
        }

        public void Notify(FileChangeEvent change)
        {
            lock (_gate)
            {
                _events.Add(change);
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _events.Clear();
            }
        }
    }

    private sealed class NullAgentEvents : IAgentEvents
    {
        public void Emit(AgentEvent agentEvent) { }
    }
}
