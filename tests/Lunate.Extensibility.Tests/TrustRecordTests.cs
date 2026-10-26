namespace Lunate.Extensibility.Tests;

/// <summary>
/// The record query behind the changed-files re-prompt wording and <c>/trust</c>: it lists both
/// sections for one repository, and clearing removes both so the next load asks again.
/// </summary>
public sealed class TrustRecordTests
{
    [Fact]
    public void Records_lists_both_sections_with_their_worktree_and_hash()
    {
        using var temp = new TempDirectory();
        var store = new ExtensionTrustStore(TestExtensions.Options(temp.Subdirectory("store")));
        store.RecordTrusted("repo-1", "/w1", "hello", "ext-hash", DateTimeOffset.UtcNow);
        store.RecordMcpConfigTrusted("repo-1", "/w1", "mcp-hash", DateTimeOffset.UtcNow);
        store.RecordTrusted("repo-2", "/w2", "hello", "other-hash", DateTimeOffset.UtcNow);

        IReadOnlyList<TrustRecord> records = store.Records("repo-1");

        Assert.Equal(
            [
                new TrustRecord(TrustSection.Extensions, "/w1", "ext-hash"),
                new TrustRecord(TrustSection.McpConfig, "/w1", "mcp-hash"),
            ],
            records
        );
    }

    [Fact]
    public void Records_orders_worktrees_within_a_section()
    {
        using var temp = new TempDirectory();
        var store = new ExtensionTrustStore(TestExtensions.Options(temp.Subdirectory("store")));
        store.RecordTrusted("repo-1", "/b", "hello", "hash-b", DateTimeOffset.UtcNow);
        store.RecordTrusted("repo-1", "/a", "hello", "hash-a", DateTimeOffset.UtcNow);

        Assert.Equal(["/a", "/b"], store.Records("repo-1").Select(record => record.WorktreePath));
    }

    [Fact]
    public void Records_is_empty_without_a_file_or_records()
    {
        using var temp = new TempDirectory();
        var store = new ExtensionTrustStore(TestExtensions.Options(temp.Subdirectory("store")));

        Assert.Empty(store.Records("repo-1"));
    }

    [Fact]
    public void ClearRepository_removes_both_sections_and_leaves_other_repositories()
    {
        using var temp = new TempDirectory();
        var store = new ExtensionTrustStore(TestExtensions.Options(temp.Subdirectory("store")));
        store.RecordTrusted("repo-1", "/w1", "hello", "ext-hash", DateTimeOffset.UtcNow);
        store.RecordMcpConfigTrusted("repo-1", "/w1", "mcp-hash", DateTimeOffset.UtcNow);
        store.RecordTrusted("repo-2", "/w2", "hello", "keep", DateTimeOffset.UtcNow);

        store.ClearRepository("repo-1");

        Assert.Empty(store.Records("repo-1"));
        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            store.Evaluate("repo-1", "/w1", "hello", "ext-hash")
        );
        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            store.EvaluateMcpConfig("repo-1", "/w1", "mcp-hash")
        );
        Assert.Equal(
            ExtensionTrustDecision.Trusted,
            store.Evaluate("repo-2", "/w2", "hello", "keep")
        );
    }

    [Fact]
    public void Clearing_an_unknown_repository_writes_nothing()
    {
        using var temp = new TempDirectory();
        string storePath = temp.Subdirectory("store");
        var store = new ExtensionTrustStore(TestExtensions.Options(storePath));

        store.ClearRepository("repo-1");

        Assert.False(File.Exists(Path.Combine(storePath, "trust.json")));
    }

    [Fact]
    public void A_new_worktree_of_a_trusted_repository_does_not_prompt()
    {
        using var temp = new TempDirectory();
        var store = new ExtensionTrustStore(TestExtensions.Options(temp.Subdirectory("store")));
        store.RecordTrusted("repo-1", "/main", "hello", "hash-v1", DateTimeOffset.UtcNow);

        // NewWorktree is the loader's no-prompt path: the new worktree is recorded silently.
        Assert.Equal(
            ExtensionTrustDecision.NewWorktree,
            store.Evaluate("repo-1", "/wt-new", "hello", "hash-v1")
        );
        Assert.Equal(
            ExtensionTrustDecision.Trusted,
            store.Evaluate("repo-1", "/main", "hello", "hash-v1")
        );
    }

    [Fact]
    public void The_content_hash_check_stays_per_worktree()
    {
        using var temp = new TempDirectory();
        var store = new ExtensionTrustStore(TestExtensions.Options(temp.Subdirectory("store")));
        store.RecordTrusted("repo-1", "/main", "hello", "hash-v1", DateTimeOffset.UtcNow);
        store.RecordTrusted("repo-1", "/wt", "hello", "hash-v1", DateTimeOffset.UtcNow);

        // The extension changed: the worktree that saw the change re-prompts...
        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            store.Evaluate("repo-1", "/main", "hello", "hash-v2")
        );
        // ...while the other worktree's own hash check is untouched by that.
        Assert.Equal(
            ExtensionTrustDecision.Trusted,
            store.Evaluate("repo-1", "/wt", "hello", "hash-v1")
        );
        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            store.Evaluate("repo-1", "/wt", "hello", "hash-v2")
        );
    }
}
