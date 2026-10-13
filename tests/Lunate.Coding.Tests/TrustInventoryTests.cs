using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

/// <summary>
/// The shared inventory behind the session's question count and the trust command: what is
/// pending, what a new worktree counts as, and what an already-trusted hash counts as.
/// </summary>
public sealed class TrustInventoryTests
{
    [Fact]
    public void An_untrusted_project_extension_is_pending()
    {
        using var temp = new TempDirectory();
        string worktree = InstallExtension(temp);
        string store = temp.File("store");

        TrustInventory inventory = Create(worktree, store);

        Assert.Equal(1, inventory.PendingCount);
        PendingExtension extension = Assert.Single(inventory.Extensions);
        Assert.Equal("hello", extension.Descriptor.Id);
        Assert.Equal(ExtensionTrustDecision.Prompt, extension.Decision);
        Assert.False(extension.FilesChanged);
        Assert.Equal(
            ExtensionTrustStore.ComputeContentHash(extension.Descriptor.Directory),
            extension.ContentHash
        );
    }

    [Fact]
    public void A_changed_hash_extension_is_pending_and_says_the_files_changed()
    {
        using var temp = new TempDirectory();
        string worktree = InstallExtension(temp);
        string store = temp.File("store");
        Store(store)
            .RecordTrusted(Identity(worktree), worktree, "a-previous-hash", DateTimeOffset.UtcNow);

        TrustInventory inventory = Create(worktree, store);

        Assert.Equal(1, inventory.PendingCount);
        Assert.True(Assert.Single(inventory.Extensions).FilesChanged);
    }

    [Fact]
    public void An_untrusted_project_mcp_config_is_pending()
    {
        using var temp = new TempDirectory();
        string worktree = InstallExtension(temp);
        WriteProjectConfig(worktree);
        string store = temp.File("store");

        TrustInventory inventory = Create(worktree, store);

        Assert.Equal(2, inventory.PendingCount);
        PendingMcpConfig config = Assert.IsType<PendingMcpConfig>(inventory.ProjectMcpConfig);
        Assert.Equal(McpComposition.ProjectConfigPath(worktree), config.Path);
        Assert.Equal(ExtensionTrustDecision.Prompt, config.Decision);
        Assert.Equal("srv", Assert.Single(config.Servers).Name);
    }

    [Fact]
    public void A_new_worktree_of_a_trusted_repository_is_not_pending()
    {
        using var temp = new TempDirectory();
        string worktree = InstallExtension(temp);
        string store = temp.File("store");
        Store(store)
            .RecordTrusted(
                Identity(worktree),
                Path.Combine(worktree, "another-worktree"),
                "a-previous-hash",
                DateTimeOffset.UtcNow
            );

        TrustInventory inventory = Create(worktree, store);

        Assert.Equal(0, inventory.PendingCount);
        Assert.Equal(
            ExtensionTrustDecision.NewWorktree,
            Assert.Single(inventory.Extensions).Decision
        );
    }

    [Fact]
    public void A_trusted_hash_and_no_config_nothing_is_pending()
    {
        using var temp = new TempDirectory();
        string worktree = InstallExtension(temp);
        string store = temp.File("store");
        string directory = Path.Combine(worktree, ".lunate", "extensions", "hello");
        Store(store)
            .RecordTrusted(
                Identity(worktree),
                worktree,
                ExtensionTrustStore.ComputeContentHash(directory),
                DateTimeOffset.UtcNow
            );

        TrustInventory inventory = Create(worktree, store);

        Assert.Equal(0, inventory.PendingCount);
        Assert.Empty(inventory.Extensions);
        Assert.Null(inventory.ProjectMcpConfig);
        Assert.NotEmpty(inventory.Records);
    }

    [Fact]
    public void No_extensions_skips_extension_discovery_but_not_the_config()
    {
        using var temp = new TempDirectory();
        string worktree = InstallExtension(temp);
        WriteProjectConfig(worktree);
        string store = temp.File("store");

        TrustInventory inventory = TrustInventory.Create(
            new Workspace(worktree),
            new ExtensionSeams { StorePath = store, NoExtensions = true }
        );

        Assert.Empty(inventory.Extensions);
        Assert.Equal(1, inventory.PendingCount);
        Assert.NotNull(inventory.ProjectMcpConfig);
    }

    private static TrustInventory Create(string worktree, string store) =>
        TrustInventory.Create(new Workspace(worktree), new ExtensionSeams { StorePath = store });

    private static string InstallExtension(TempDirectory temp)
    {
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        HelloFixture.Install(Path.Combine(worktree, ".lunate", "extensions", "hello"));
        return Workspace.Canonicalize(worktree);
    }

    private static void WriteProjectConfig(string worktree)
    {
        string path = McpComposition.ProjectConfigPath(worktree);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "mcpServers": { "srv": { "command": "x" } } }""");
    }

    private static string Identity(string worktree) => new Workspace(worktree).GitCommonDir!;

    private static ExtensionTrustStore Store(string storePath) =>
        new(new ExtensionHostOptions { StorePath = storePath });
}
