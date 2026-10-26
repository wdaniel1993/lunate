using System.Text.Json;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The mcp.json trust record lives beside extension trust in the same file but in its own section:
/// recording config trust must never read or write the extension records, and a changed file hash
/// re-prompts.
/// </summary>
public sealed class McpConfigTrustStoreTests
{
    [Fact]
    public void Config_trust_round_trips_in_its_own_section()
    {
        using var temp = new TempDirectory();
        var store = new ExtensionTrustStore(TestExtensions.Options(temp.Subdirectory("store")));
        string config = temp.File("mcp.json");
        File.WriteAllText(config, """{ "mcpServers": {} }""");
        string hash = ExtensionTrustStore.ComputeFileContentHash(config);

        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            store.EvaluateMcpConfig("repo-1", "/worktree", hash)
        );

        store.RecordMcpConfigTrusted("repo-1", "/worktree", hash, DateTimeOffset.UnixEpoch);

        Assert.Equal(
            ExtensionTrustDecision.Trusted,
            store.EvaluateMcpConfig("repo-1", "/worktree", hash)
        );
    }

    [Fact]
    public void Config_trust_never_answers_extension_trust_and_vice_versa()
    {
        using var temp = new TempDirectory();
        string storePath = temp.Subdirectory("store");
        var store = new ExtensionTrustStore(TestExtensions.Options(storePath));

        store.RecordMcpConfigTrusted("repo-1", "/worktree", "config-hash", DateTimeOffset.UtcNow);

        Assert.Equal(
            ExtensionTrustDecision.Trusted,
            store.EvaluateMcpConfig("repo-1", "/worktree", "config-hash")
        );
        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            store.Evaluate("repo-1", "/worktree", "hello", "config-hash")
        );

        store.RecordTrusted(
            "repo-1",
            "/worktree",
            "hello",
            "extension-hash",
            DateTimeOffset.UtcNow
        );

        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            store.EvaluateMcpConfig("repo-1", "/worktree", "extension-hash")
        );
        Assert.Equal(
            ExtensionTrustDecision.Trusted,
            store.Evaluate("repo-1", "/worktree", "hello", "extension-hash")
        );

        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(storePath, "trust.json"))
        );
        Assert.True(document.RootElement.TryGetProperty("repositories", out _));
        Assert.True(document.RootElement.TryGetProperty("mcpConfig", out _));
    }

    [Fact]
    public void A_store_in_the_previous_layout_prompts_and_accepts_the_new_records()
    {
        using var temp = new TempDirectory();
        string storePath = temp.Subdirectory("store");
        Directory.CreateDirectory(storePath);

        // The layout the previous version wrote (git show main:ExtensionTrustStore.cs): mcp.json
        // trust lived under a worktree-keyed map, and there was no per-server section. The new
        // keying cannot match those records, so both decisions must ask again — never silently
        // record the current file — and the new records must coexist with the old shape.
        File.WriteAllText(
            Path.Combine(storePath, "trust.json"),
            """
            {
              "repositories": {
                "repo-1": {
                  "trustedAt": "2026-10-15T00:00:00.0000000+00:00",
                  "worktrees": { "/main": "extension-hash" }
                }
              },
              "mcpConfig": {
                "repo-1": {
                  "trustedAt": "2026-10-15T00:00:00.0000000+00:00",
                  "worktrees": { "/main": "config-hash" }
                }
              }
            }
            """
        );

        var store = new ExtensionTrustStore(TestExtensions.Options(storePath));
        const string ConfigPath = "/main/.lunate/mcp.json";

        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            store.EvaluateMcpConfig("repo-1", ConfigPath, "new-config-hash")
        );
        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            store.EvaluateMcpServer("repo-1", "definition-hash")
        );

        store.RecordMcpConfigTrusted(
            "repo-1",
            ConfigPath,
            "new-config-hash",
            DateTimeOffset.UnixEpoch
        );
        store.RecordMcpServerTrusted(
            "repo-1",
            "/main",
            "definition-hash",
            DateTimeOffset.UnixEpoch
        );

        Assert.Equal(
            ExtensionTrustDecision.Trusted,
            store.EvaluateMcpConfig("repo-1", ConfigPath, "new-config-hash")
        );
        Assert.Equal(
            ExtensionTrustDecision.Trusted,
            store.EvaluateMcpServer("repo-1", "definition-hash")
        );
    }

    [Fact]
    public void A_changed_file_hash_is_a_prompt_again()
    {
        using var temp = new TempDirectory();
        string path = temp.File("mcp.json");
        File.WriteAllText(path, "one");
        string first = ExtensionTrustStore.ComputeFileContentHash(path);
        File.WriteAllText(path, "two");
        string second = ExtensionTrustStore.ComputeFileContentHash(path);

        Assert.NotEqual(first, second);

        var store = new ExtensionTrustStore(TestExtensions.Options(temp.Subdirectory("store")));
        store.RecordMcpConfigTrusted("repo-1", "/worktree", first, DateTimeOffset.UtcNow);

        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            store.EvaluateMcpConfig("repo-1", "/worktree", second)
        );
    }

    [Fact]
    public void A_missing_file_hashes_to_the_empty_hash() =>
        Assert.Equal(
            ExtensionTrustStore.ComputeFileContentHash(Path.Combine("no", "such", "mcp.json")),
            ExtensionTrustStore.ComputeFileContentHash(Path.Combine("other", "mcp.json"))
        );
}
