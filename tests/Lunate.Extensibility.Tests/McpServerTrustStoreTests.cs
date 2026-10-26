using System.Text.Json;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The per-server approval records: keyed by repository identity and definition hash in their own
/// section (an approval never auto-answers a different repository, definition, file or
/// extension), carrying the approving worktree, and cleared with the repository's other records.
/// </summary>
public sealed class McpServerTrustStoreTests
{
    [Fact]
    public void A_definition_is_prompted_until_recorded()
    {
        using var temp = new TempDirectory();
        var store = new ExtensionTrustStore(TestExtensions.Options(temp.Subdirectory("store")));

        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            store.EvaluateMcpServer("repo-1", "definition-hash")
        );

        store.RecordMcpServerTrusted(
            "repo-1",
            "/worktree",
            "definition-hash",
            DateTimeOffset.UtcNow
        );

        Assert.Equal(
            ExtensionTrustDecision.Trusted,
            store.EvaluateMcpServer("repo-1", "definition-hash")
        );
    }

    [Fact]
    public void A_changed_definition_prompts_again()
    {
        using var temp = new TempDirectory();
        var store = new ExtensionTrustStore(TestExtensions.Options(temp.Subdirectory("store")));
        store.RecordMcpServerTrusted("repo-1", "/worktree", "old-hash", DateTimeOffset.UtcNow);

        Assert.Equal(ExtensionTrustDecision.Prompt, store.EvaluateMcpServer("repo-1", "new-hash"));
    }

    [Fact]
    public void The_same_definition_in_another_repository_still_prompts()
    {
        using var temp = new TempDirectory();
        var store = new ExtensionTrustStore(TestExtensions.Options(temp.Subdirectory("store")));
        store.RecordMcpServerTrusted(
            "repo-1",
            "/worktree",
            "definition-hash",
            DateTimeOffset.UtcNow
        );

        Assert.Equal(
            ExtensionTrustDecision.Trusted,
            store.EvaluateMcpServer("repo-1", "definition-hash")
        );
        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            store.EvaluateMcpServer("repo-2", "definition-hash")
        );
    }

    [Fact]
    public void A_record_carries_the_approving_worktree()
    {
        using var temp = new TempDirectory();
        var store = new ExtensionTrustStore(TestExtensions.Options(temp.Subdirectory("store")));
        store.RecordMcpServerTrusted(
            "repo-1",
            "/approving-worktree",
            "definition-hash",
            DateTimeOffset.UtcNow
        );

        TrustRecord record = Assert.Single(store.Records("repo-1"));

        Assert.Equal(TrustSection.McpServer, record.Section);
        Assert.Equal("/approving-worktree", record.WorktreePath);
        Assert.Equal("definition-hash", record.ContentHash);
    }

    [Fact]
    public void Server_approvals_never_answer_the_other_sections()
    {
        using var temp = new TempDirectory();
        string storePath = temp.Subdirectory("store");
        var store = new ExtensionTrustStore(TestExtensions.Options(storePath));
        store.RecordMcpServerTrusted("repo-1", "/worktree", "server-hash", DateTimeOffset.UtcNow);

        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            store.EvaluateMcpConfig("repo-1", "/worktree", "server-hash")
        );
        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            store.Evaluate("repo-1", "/worktree", "hello", "server-hash")
        );

        store.RecordMcpConfigTrusted("repo-1", "/worktree", "config-hash", DateTimeOffset.UtcNow);
        store.RecordTrusted(
            "repo-1",
            "/worktree",
            "hello",
            "extension-hash",
            DateTimeOffset.UtcNow
        );

        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            store.EvaluateMcpServer("repo-1", "config-hash")
        );
        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            store.EvaluateMcpServer("repo-1", "extension-hash")
        );

        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(storePath, "trust.json"))
        );
        Assert.True(document.RootElement.TryGetProperty("mcpServers", out _));
    }

    [Fact]
    public void Clear_removes_the_per_server_records_with_the_repositorys_others()
    {
        using var temp = new TempDirectory();
        var store = new ExtensionTrustStore(TestExtensions.Options(temp.Subdirectory("store")));
        store.RecordTrusted(
            "repo-1",
            "/worktree",
            "hello",
            "extension-hash",
            DateTimeOffset.UtcNow
        );
        store.RecordMcpConfigTrusted("repo-1", "/worktree", "config-hash", DateTimeOffset.UtcNow);
        store.RecordMcpServerTrusted("repo-1", "/worktree", "server-hash", DateTimeOffset.UtcNow);
        store.RecordMcpServerTrusted("repo-2", "/worktree", "other-hash", DateTimeOffset.UtcNow);

        store.ClearRepository("repo-1");

        Assert.Empty(store.Records("repo-1"));
        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            store.EvaluateMcpServer("repo-1", "server-hash")
        );
        Assert.Single(store.Records("repo-2"));
    }
}
