using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The stamp backstop of the bash change tracker (#418): when git cannot look — a broken HEAD on
/// a starved sharded leg — a scan must still answer from content stamps, and a scan that could
/// not look at all must stay distinguishable from one that looked and saw nothing.
/// </summary>
public sealed class BashChangeStampBackstopTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>The CI shape: a HEAD resolving to a sha whose object is missing.</summary>
    private static void BreakHead(string repoRoot) =>
        File.WriteAllText(
            Path.Combine(repoRoot, ".git", "HEAD"),
            "0000000000000000000000000000000000000001\n"
        );

    [Fact]
    public async Task An_edit_behind_a_broken_head_reaches_the_stamp_backstop()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        BreakHead(repo.Root);
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);

        ScanReport first = await tracker.ObserveAsync("run_1", repo.Root, Ct);

        Assert.Empty(first.Changes);
        Assert.Equal(ScanSource.Stamp, first.Source);
        Assert.NotNull(first.FailureReason);

        repo.Write("a.cs", "class A { int x; }\n");
        ScanReport second = await tracker.ObserveAsync("run_1", repo.Root, Ct);

        Assert.Equal(
            [new BashChangeTracker.Change("a.cs", FileChangeKind.Modified)],
            second.Changes
        );
        Assert.Equal(ScanSource.Stamp, second.Source);
    }

    [Fact]
    public async Task An_stamped_edit_publishes_when_git_breaks_after_a_healthy_baseline()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        repo.Write("a.cs", "class A { int dirty; }\n");
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);

        ScanReport healthyBaseline = await tracker.ObserveAsync("run_1", repo.Root, Ct);

        Assert.Empty(healthyBaseline.Changes);
        Assert.Equal(ScanSource.Git, healthyBaseline.Source);

        // Git answers at the baseline and breaks later; a revert by direct file write (git cannot
        // run behind a broken HEAD) still publishes, because the stamp differs from the healthy
        // git snapshot.
        BreakHead(repo.Root);
        repo.Write("a.cs", "class A { }\n");
        ScanReport reverted = await tracker.ObserveAsync("run_1", repo.Root, Ct);

        Assert.Equal(
            [new BashChangeTracker.Change("a.cs", FileChangeKind.Modified)],
            reverted.Changes
        );
        Assert.Equal(ScanSource.Stamp, reverted.Source);

        repo.Write("a.cs", "class A { int y; }\n");
        ScanReport edited = await tracker.ObserveAsync("run_1", repo.Root, Ct);

        Assert.Equal(
            [new BashChangeTracker.Change("a.cs", FileChangeKind.Modified)],
            edited.Changes
        );
        Assert.Equal(ScanSource.Stamp, edited.Source);
    }

    [Fact]
    public async Task A_scan_that_cannot_look_is_distinct_from_no_change()
    {
        using var temp = new TempDirectory();
        Directory.Delete(temp.Root, recursive: true);
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);

        ScanReport report = await tracker.ObserveAsync("run_1", temp.Root, Ct);

        // "Could not look" is not "nothing changed" (#418): the source says so and the reason
        // names why — a caller reading a bare empty list must not take it for a quiet workspace.
        Assert.Equal(ScanSource.Unavailable, report.Source);
        Assert.Empty(report.Changes);
        Assert.False(
            string.IsNullOrWhiteSpace(report.FailureReason),
            "an unavailable scan must say why it produced nothing"
        );
    }
}
