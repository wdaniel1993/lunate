using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Testing;

namespace WorktreeTasksExtension.Tests;

/// <summary>
/// The refusal edges over real git repositories: a 'base' that is really a command-line option is
/// refused before git sees it, and a merge failure that is not a conflict reports git's own words
/// without pretending to roll anything back.
/// </summary>
public sealed class WorktreeTasksRefusalTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_base_that_starts_with_a_dash_is_refused()
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

        ToolResult refusal = await start.ExecuteAsync(
            JsonSerializer.SerializeToElement(
                new
                {
                    name = "wt-a",
                    task = "x",
                    @base = "--detach",
                }
            ),
            context,
            Ct
        );

        Assert.True(refusal.IsError);
        Assert.Contains("'base'", refusal.Output, StringComparison.Ordinal);
        Assert.Contains("--detach", refusal.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(TestSupport.WorktreePath(host, "wt-a")));
    }

    [Fact]
    public async Task An_unrelated_history_merge_reports_git_without_an_abort_attempt()
    {
        using var temp = new TempDirectory();
        await using ExtensionTestHost host = TestSupport.CreateHost(temp);
        await host.StartAsync(Ct);
        await TestSupport.InitRepositoryAsync(host, "base.txt");
        host.ComposeTools();
        var runner = TestSupport.ProcessRunner(host);

        // An orphan branch shares no history with the target: git refuses the merge up front.
        (int orphanCode, string orphanOutput) = await TestSupport.GitAsync(
            host,
            runner,
            "checkout",
            "--orphan",
            "lunate/wt-a"
        );
        Assert.True(orphanCode == 0, $"git checkout --orphan failed: {orphanOutput}");
        (int commitCode, string commitOutput) = await TestSupport.GitAsync(
            host,
            runner,
            "commit",
            "--allow-empty",
            "-q",
            "-m",
            "orphan"
        );
        Assert.True(commitCode == 0, $"committing the orphan branch failed: {commitOutput}");
        (int backCode, string backOutput) = await TestSupport.GitAsync(
            host,
            runner,
            "checkout",
            "main"
        );
        Assert.True(backCode == 0, $"git checkout main failed: {backOutput}");

        // The same merge outside the tool captures git's own words, locale included.
        (int preCode, string preOutput) = await TestSupport.GitAsync(
            host,
            runner,
            "merge",
            "--no-ff",
            "lunate/wt-a"
        );
        Assert.NotEqual(0, preCode);

        ITool merge = host.Tools.Find("worktree__merge")!;
        ToolResult refusal = await merge.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { name = "wt-a" }),
            new ToolContext(host.WorkingDirectory, new NoopAgentEvents()),
            Ct
        );

        Assert.True(refusal.IsError);
        Assert.Contains(preOutput.Trim(), refusal.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("abort", refusal.Output, StringComparison.OrdinalIgnoreCase);
        (int headCode, _) = await TestSupport.GitAsync(
            host,
            runner,
            "rev-parse",
            "--verify",
            "--quiet",
            "MERGE_HEAD"
        );
        Assert.NotEqual(0, headCode);
    }
}
