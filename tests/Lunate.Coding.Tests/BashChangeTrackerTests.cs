using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The bash change tracker's rules, against real repositories: only real changes are published —
/// never raw status (which would re-flag already-dirty files every call) — and reverts are caught
/// while commits stay quiet.
/// </summary>
public sealed class BashChangeTrackerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public void Parse_handles_modified_untracked_renamed_and_deleted_entries()
    {
        const string porcelain =
            " M one.cs\0?? new.cs\0?? newdir/\0R  renamed.cs\0old.cs\0D  gone.cs\0";

        var entries = BashChangeTracker.ParsePorcelain(porcelain);

        Assert.Equal(
            [
                ("one.cs", FileChangeKind.Modified),
                ("new.cs", FileChangeKind.Created),
                ("renamed.cs", FileChangeKind.Created),
                ("old.cs", FileChangeKind.Modified),
                ("gone.cs", FileChangeKind.Modified),
            ],
            entries
        );
    }

    [Fact]
    public async Task The_first_scan_of_a_run_only_baselines()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        repo.Write("a.cs", "class A { int x; }\n");
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);

        IReadOnlyList<BashChangeTracker.Change> first = await tracker.ObserveAsync(
            "run_1",
            repo.Root,
            Ct
        );
        IReadOnlyList<BashChangeTracker.Change> second = await tracker.ObserveAsync(
            "run_1",
            repo.Root,
            Ct
        );

        Assert.Empty(first);
        Assert.Empty(second);
    }

    [Fact]
    public async Task A_new_change_is_published_once()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);
        await tracker.ObserveAsync("run_1", repo.Root, Ct);

        repo.Write("b.cs", "class B { }\n");
        IReadOnlyList<BashChangeTracker.Change> changes = await tracker.ObserveAsync(
            "run_1",
            repo.Root,
            Ct
        );
        IReadOnlyList<BashChangeTracker.Change> again = await tracker.ObserveAsync(
            "run_1",
            repo.Root,
            Ct
        );

        Assert.Equal([new BashChangeTracker.Change("b.cs", FileChangeKind.Created)], changes);
        Assert.Empty(again);
    }

    [Fact]
    public async Task An_already_dirty_file_is_not_re_flagged_by_an_unrelated_call()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        repo.Write("a.cs", "class A { int dirty; }\n");
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);
        await tracker.ObserveAsync("run_1", repo.Root, Ct);

        repo.Write("unrelated.cs", "class U { }\n");
        IReadOnlyList<BashChangeTracker.Change> changes = await tracker.ObserveAsync(
            "run_1",
            repo.Root,
            Ct
        );

        Assert.Equal(
            [new BashChangeTracker.Change("unrelated.cs", FileChangeKind.Created)],
            changes
        );
    }

    [Fact]
    public async Task A_same_stamp_edit_to_a_dirty_file_is_caught()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        string file = repo.Write("a.cs", "class A { int x; }\n");
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);
        await tracker.ObserveAsync("run_1", repo.Root, Ct);

        DateTime stamp = File.GetLastWriteTimeUtc(file);
        repo.Write("a.cs", "class A { int y; }\n");
        File.SetLastWriteTimeUtc(file, stamp);
        IReadOnlyList<BashChangeTracker.Change> changes = await tracker.ObserveAsync(
            "run_1",
            repo.Root,
            Ct
        );

        Assert.Equal([new BashChangeTracker.Change("a.cs", FileChangeKind.Modified)], changes);
    }

    [Fact]
    public async Task A_revert_is_published()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        repo.Write("a.cs", "class A { int dirty; }\n");
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);
        await tracker.ObserveAsync("run_1", repo.Root, Ct);

        repo.Run("checkout", "--", "a.cs");
        IReadOnlyList<BashChangeTracker.Change> changes = await tracker.ObserveAsync(
            "run_1",
            repo.Root,
            Ct
        );

        Assert.Equal([new BashChangeTracker.Change("a.cs", FileChangeKind.Modified)], changes);
    }

    [Fact]
    public async Task A_commit_is_not_flagged()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        repo.Write("a.cs", "class A { int committed; }\n");
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);
        await tracker.ObserveAsync("run_1", repo.Root, Ct);

        repo.CommitAll("the dirty file");
        IReadOnlyList<BashChangeTracker.Change> changes = await tracker.ObserveAsync(
            "run_1",
            repo.Root,
            Ct
        );

        Assert.Empty(changes);
    }

    [Fact]
    public async Task A_branch_switch_publishes_the_content_changed_files()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.Write("shared.cs", "class Shared { }\n");
        repo.CommitAll();
        repo.Run("checkout", "-q", "-b", "feature");
        repo.Write("a.cs", "class A { int feature; }\n");
        repo.CommitAll("feature work");
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);
        await tracker.ObserveAsync("run_1", repo.Root, Ct);

        repo.Run("checkout", "-q", "-");
        IReadOnlyList<BashChangeTracker.Change> changes = await tracker.ObserveAsync(
            "run_1",
            repo.Root,
            Ct
        );

        Assert.Equal([new BashChangeTracker.Change("a.cs", FileChangeKind.Modified)], changes);
    }

    [Fact]
    public async Task Untracked_directories_are_skipped_untracked_files_are_published()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);
        await tracker.ObserveAsync("run_1", repo.Root, Ct);

        repo.Write("newdir/inner.cs", "class Inner { }\n");
        repo.Write("newfile.cs", "class NewFile { }\n");
        IReadOnlyList<BashChangeTracker.Change> changes = await tracker.ObserveAsync(
            "run_1",
            repo.Root,
            Ct
        );

        Assert.Equal([new BashChangeTracker.Change("newfile.cs", FileChangeKind.Created)], changes);
    }

    [Fact]
    public async Task A_plain_move_shows_as_delete_and_untracked()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);
        await tracker.ObserveAsync("run_1", repo.Root, Ct);

        File.Move(Path.Combine(repo.Root, "a.cs"), Path.Combine(repo.Root, "b.cs"));
        IReadOnlyList<BashChangeTracker.Change> changes = await tracker.ObserveAsync(
            "run_1",
            repo.Root,
            Ct
        );

        Assert.Equal(
            [
                new BashChangeTracker.Change("a.cs", FileChangeKind.Modified),
                new BashChangeTracker.Change("b.cs", FileChangeKind.Created),
            ],
            changes.OrderBy(change => change.RelativePath, StringComparer.Ordinal)
        );
    }

    [Fact]
    public async Task A_staged_rename_publishes_both_paths()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);
        await tracker.ObserveAsync("run_1", repo.Root, Ct);

        repo.Run("mv", "a.cs", "renamed.cs");
        repo.Run("add", "-A");
        IReadOnlyList<BashChangeTracker.Change> changes = await tracker.ObserveAsync(
            "run_1",
            repo.Root,
            Ct
        );

        Assert.Equal(
            [
                new BashChangeTracker.Change("a.cs", FileChangeKind.Modified),
                new BashChangeTracker.Change("renamed.cs", FileChangeKind.Created),
            ],
            changes.OrderBy(change => change.RelativePath, StringComparer.Ordinal)
        );
    }

    [Fact]
    public async Task A_non_repository_root_yields_nothing()
    {
        using var temp = new TempDirectory();
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);

        IReadOnlyList<BashChangeTracker.Change> changes = await tracker.ObserveAsync(
            "run_1",
            temp.Root,
            Ct
        );

        Assert.Empty(changes);
    }

    [Fact]
    public async Task A_workspace_subdirectory_maps_paths_relative_to_the_root()
    {
        using var repo = new GitTestRepo();
        repo.Write("sub/a.cs", "class A { }\n");
        repo.Write("outside.cs", "class Outside { }\n");
        repo.CommitAll();
        string sub = Path.Combine(repo.Root, "sub");
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);
        await tracker.ObserveAsync("run_1", sub, Ct);

        repo.Write("sub/a.cs", "class A { int changed; }\n");
        repo.Write("outside.cs", "class Outside { int changed; }\n");
        IReadOnlyList<BashChangeTracker.Change> changes = await tracker.ObserveAsync(
            "run_1",
            sub,
            Ct
        );

        Assert.Equal([new BashChangeTracker.Change("a.cs", FileChangeKind.Modified)], changes);
    }

    [Fact]
    public async Task An_edit_then_a_commit_is_not_republished()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);
        await tracker.ObserveAsync("run_1", repo.Root, Ct);
        repo.Write("a.cs", "class A { int x; }\n");
        Assert.Single(await tracker.ObserveAsync("run_1", repo.Root, Ct));

        // The commit moves HEAD and the content is unchanged: nothing may be published.
        repo.CommitAll();
        IReadOnlyList<BashChangeTracker.Change> committed = await tracker.ObserveAsync(
            "run_1",
            repo.Root,
            Ct
        );

        Assert.Empty(committed);
    }

    [Fact]
    public async Task A_revert_of_a_mid_run_edit_is_published()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);
        await tracker.ObserveAsync("run_1", repo.Root, Ct);
        repo.Write("a.cs", "class A { int x; }\n");
        Assert.Single(await tracker.ObserveAsync("run_1", repo.Root, Ct));

        repo.Run("checkout", "--", "a.cs");
        IReadOnlyList<BashChangeTracker.Change> reverted = await tracker.ObserveAsync(
            "run_1",
            repo.Root,
            Ct
        );

        Assert.Equal([new BashChangeTracker.Change("a.cs", FileChangeKind.Modified)], reverted);
    }

    [Fact]
    public void Parse_consumes_copy_origins_without_a_bogus_entry()
    {
        const string porcelain = "C  new.cs\0old.cs\0";

        var entries = BashChangeTracker.ParsePorcelain(porcelain);

        Assert.Equal([("new.cs", FileChangeKind.Created)], entries);
    }

    [Fact]
    public async Task A_committed_rename_publishes_the_new_side_as_created()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);
        await tracker.ObserveAsync("run_1", repo.Root, Ct);

        repo.Run("mv", "a.cs", "b.cs");
        repo.CommitAll();
        IReadOnlyList<BashChangeTracker.Change> changes = await tracker.ObserveAsync(
            "run_1",
            repo.Root,
            Ct
        );

        Assert.Equal(
            [
                new BashChangeTracker.Change("a.cs", FileChangeKind.Modified),
                new BashChangeTracker.Change("b.cs", FileChangeKind.Created),
            ],
            changes
        );
    }

    [Fact]
    public async Task The_least_recently_used_snapshot_is_evicted_past_the_cap()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        var tracker = new BashChangeTracker(scanTimeout: WaitBudgets.BashScan);
        await tracker.ObserveAsync("run_old", repo.Root, Ct);
        repo.Write("a.cs", "class A { int x; }\n");
        Assert.Single(await tracker.ObserveAsync("run_old", repo.Root, Ct));

        for (int i = 0; i < BashChangeTracker.MaxSnapshots; i++)
        {
            await tracker.ObserveAsync($"run_{i}", repo.Root, Ct);
        }

        // The oldest snapshot was evicted, so this scan baselines afresh and publishes nothing.
        repo.Write("a.cs", "class A { int y; }\n");
        IReadOnlyList<BashChangeTracker.Change> afterEviction = await tracker.ObserveAsync(
            "run_old",
            repo.Root,
            Ct
        );

        Assert.Empty(afterEviction);
    }

    [Fact]
    public async Task A_scan_timeout_names_its_reason_in_the_log()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        var log = new RecordingExtensionLog();
        var tracker = new BashChangeTracker(log, scanTimeout: WaitBudgets.CollapsedScan);

        IReadOnlyList<BashChangeTracker.Change> changes = await tracker.ObserveAsync(
            "run_1",
            repo.Root,
            Ct
        );

        // No events (the backstop holds) and no silence: the extension log is where a scan that
        // produced nothing explains itself, so a missed publish is a named failure (#395).
        Assert.Empty(changes);
        Assert.True(
            log.Contains("bash change scan timed out"),
            $"the log said nothing about the scan: {string.Join(Environment.NewLine, log.Messages)}"
        );
    }

    [Fact]
    public async Task A_failed_git_status_names_the_reason_in_the_log()
    {
        using var repo = new GitTestRepo();
        repo.Write("a.cs", "class A { }\n");
        repo.CommitAll();
        File.Delete(Path.Combine(repo.Root, ".git", "HEAD"));
        var log = new RecordingExtensionLog();
        var tracker = new BashChangeTracker(log, scanTimeout: WaitBudgets.BashScan);

        IReadOnlyList<BashChangeTracker.Change> changes = await tracker.ObserveAsync(
            "run_1",
            repo.Root,
            Ct
        );

        // A git that exits non-zero used to be silent — the exact shape that let a starved
        // sharded leg run three CI passes without a publish and without a hint (#395).
        Assert.Empty(changes);
        Assert.True(
            log.Contains("bash change scan: git status failed"),
            $"the log said nothing about the scan: {string.Join(Environment.NewLine, log.Messages)}"
        );
    }
}
