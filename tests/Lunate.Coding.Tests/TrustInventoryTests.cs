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

        Assert.Equal(3, inventory.PendingCount);
        PendingMcpConfig config = Assert.Single(inventory.ProjectMcpConfigs);
        Assert.Equal(McpComposition.ProjectConfigPath(worktree), config.Path);
        Assert.Equal(ExtensionTrustDecision.Prompt, config.Decision);
        Assert.Equal("srv", Assert.Single(config.Servers).Name);
        Assert.Equal("srv", Assert.Single(inventory.McpServers).Definition.Name);
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
        Assert.Empty(inventory.ProjectMcpConfigs);
        Assert.Empty(inventory.McpServers);
        Assert.NotEmpty(inventory.Records);
    }

    [Fact]
    public void The_digest_is_machine_independent_and_content_sensitive()
    {
        using var temp = new TempDirectory();
        string firstWorktree = InstallProject(temp, "first-worktree");
        string secondWorktree = InstallProject(temp, "second-worktree");
        string store = temp.File("store");

        string? first = Create(firstWorktree, store).Digest;
        string? second = Create(secondWorktree, store).Digest;

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first, second);

        File.WriteAllText(
            McpComposition.ProjectConfigPath(secondWorktree),
            """{ "mcpServers": { "srv": { "command": "y" } } }"""
        );
        string? changed = Create(secondWorktree, store).Digest;

        Assert.NotNull(changed);
        Assert.NotEqual(first, changed);
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
        Assert.Equal(2, inventory.PendingCount);
        Assert.Single(inventory.ProjectMcpConfigs);
    }

    [Fact]
    public void A_trusted_file_with_an_unapproved_server_still_pends_per_server()
    {
        using var temp = new TempDirectory();
        string worktree = InstallProject(temp, "worktree");
        string store = temp.File("store");
        Store(store)
            .RecordMcpConfigTrusted(
                Identity(worktree),
                McpComposition.ProjectConfigPath(worktree),
                McpConfigTrustHash.ComputeFile(McpComposition.ProjectConfigPath(worktree)),
                DateTimeOffset.UtcNow
            );

        TrustInventory inventory = Create(worktree, store);

        Assert.Empty(inventory.ProjectMcpConfigs);
        PendingMcpServer server = Assert.Single(inventory.McpServers);
        Assert.Equal("srv", server.Definition.Name);
        Assert.Equal(McpConfigTrustHash.ComputeDefinition(server.Definition), server.Hash);
    }

    [Fact]
    public void Each_project_file_pends_its_file_decision_and_its_servers()
    {
        using var temp = new TempDirectory();
        string worktree = Workspace.Canonicalize(temp.File("worktree"));
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        Directory.CreateDirectory(Path.Combine(worktree, ".lunate"));
        File.WriteAllText(
            McpComposition.ProjectConfigPath(worktree),
            """{ "mcpServers": { "one": { "command": "x" } } }"""
        );
        Directory.CreateDirectory(Path.Combine(worktree, ".agents"));
        File.WriteAllText(
            McpComposition.ProjectAgentsConfigPath(worktree),
            """{ "mcpServers": { "two": { "command": "x" } } }"""
        );
        File.WriteAllText(
            McpComposition.ProjectRootConfigPath(worktree),
            """{ "mcpServers": { "three": { "command": "x" } } }"""
        );

        TrustInventory inventory = Create(worktree, temp.File("store"));

        Assert.Equal(6, inventory.PendingCount);
        Assert.Equal(
            [
                McpComposition.ProjectConfigPath(worktree),
                McpComposition.ProjectAgentsConfigPath(worktree),
                McpComposition.ProjectRootConfigPath(worktree),
            ],
            inventory.ProjectMcpConfigs.Select(config => config.Path)
        );
        Assert.Equal(
            ["one", "two", "three"],
            inventory.McpServers.Select(server => server.Definition.Name)
        );
    }

    [Fact]
    public void The_digest_covers_per_server_decisions()
    {
        using var temp = new TempDirectory();
        string worktree = InstallProject(temp, "worktree");
        string store = temp.File("store");
        string config = McpComposition.ProjectConfigPath(worktree);
        Store(store)
            .RecordMcpConfigTrusted(
                Identity(worktree),
                McpComposition.ProjectConfigPath(worktree),
                McpConfigTrustHash.ComputeFile(config),
                DateTimeOffset.UtcNow
            );

        string? first = Create(worktree, store).Digest;

        File.WriteAllText(config, """{ "mcpServers": { "srv": { "command": "y" } } }""");
        Store(store)
            .RecordMcpConfigTrusted(
                Identity(worktree),
                McpComposition.ProjectConfigPath(worktree),
                McpConfigTrustHash.ComputeFile(config),
                DateTimeOffset.UtcNow
            );
        string? changed = Create(worktree, store).Digest;

        Assert.NotNull(first);
        Assert.NotNull(changed);
        Assert.NotEqual(first, changed);
    }

    [Fact]
    public void The_digest_ignores_a_pure_line_ending_change_in_a_project_extension()
    {
        using var temp = new TempDirectory();
        string worktree = InstallExtension(temp);
        string store = temp.File("store");
        string manifest = Path.Combine(
            worktree,
            ".lunate",
            "extensions",
            "hello",
            "extension.json"
        );

        string? before = Create(worktree, store).Digest;

        File.WriteAllText(manifest, File.ReadAllText(manifest).ReplaceLineEndings("\r\n"));
        string? after = Create(worktree, store).Digest;

        Assert.NotNull(before);
        Assert.Equal(before, after);
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

    private static string InstallProject(TempDirectory temp, string name)
    {
        string worktree = temp.File(name);
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        HelloFixture.Install(Path.Combine(worktree, ".lunate", "extensions", "hello"));
        WriteProjectConfig(worktree);
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
