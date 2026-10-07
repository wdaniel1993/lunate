using System.Text.Json;

namespace Lunate.Extensibility.Tests;

public sealed class ExtensionTrustTests
{
    [Fact]
    public async Task An_untrusted_project_extension_prompts_once_and_records_the_decision()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        string worktree = temp.Subdirectory("worktree");
        TestExtensions.InstallProjectHelloExtension(worktree, "hello");
        var loader = new ExtensionLoader(TestExtensions.Options(store));
        loader.Discover(worktree, "repo-1");
        var prompt = new FakeExtensionTrustPrompt(approve: true);

        await loader.Load(
            "hello",
            worktree,
            "repo-1",
            prompt,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, prompt.Calls);
        string trustPath = Path.Combine(store, "trust.json");
        Assert.Equal(["trust.json"], Directory.GetFiles(store).Select(Path.GetFileName));

        loader.Unload("hello");
        await loader.Load(
            "hello",
            worktree,
            "repo-1",
            prompt,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, prompt.Calls);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(trustPath));
        JsonElement repository = document
            .RootElement.GetProperty("repositories")
            .GetProperty("repo-1");
        Assert.False(string.IsNullOrWhiteSpace(repository.GetProperty("trustedAt").GetString()));
        Assert.True(repository.GetProperty("worktrees").TryGetProperty(worktree, out _));
    }

    [Fact]
    public async Task A_new_worktree_of_a_trusted_repository_does_not_re_prompt()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        string first = temp.Subdirectory("worktree-1");
        string second = temp.Subdirectory("worktree-2");
        TestExtensions.InstallProjectHelloExtension(first, "hello");
        TestExtensions.InstallProjectHelloExtension(second, "hello");
        var prompt = new FakeExtensionTrustPrompt(approve: true);
        var loader = new ExtensionLoader(TestExtensions.Options(store));

        loader.Discover(first, "repo-1");
        await loader.Load("hello", first, "repo-1", prompt, TestContext.Current.CancellationToken);
        loader.Unload("hello");
        loader.Discover(second, "repo-1");
        await loader.Load("hello", second, "repo-1", prompt, TestContext.Current.CancellationToken);

        Assert.Equal(1, prompt.Calls);
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(store, "trust.json"))
        );
        JsonElement worktrees = document
            .RootElement.GetProperty("repositories")
            .GetProperty("repo-1")
            .GetProperty("worktrees");
        Assert.True(worktrees.TryGetProperty(first, out _));
        Assert.True(worktrees.TryGetProperty(second, out _));
    }

    [Fact]
    public async Task A_changed_content_hash_re_prompts()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        string worktree = temp.Subdirectory("worktree");
        string directory = TestExtensions.InstallProjectHelloExtension(worktree, "hello");
        var loader = new ExtensionLoader(TestExtensions.Options(store));
        loader.Discover(worktree, "repo-1");
        var prompt = new FakeExtensionTrustPrompt(approve: true);

        await loader.Load(
            "hello",
            worktree,
            "repo-1",
            prompt,
            TestContext.Current.CancellationToken
        );
        loader.Unload("hello");
        File.WriteAllText(Path.Combine(directory, "changed.txt"), "changed");
        await loader.Load(
            "hello",
            worktree,
            "repo-1",
            prompt,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, prompt.Calls);
    }

    [Fact]
    public async Task Denial_refuses_the_load()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        string worktree = temp.Subdirectory("worktree");
        TestExtensions.InstallProjectHelloExtension(worktree, "hello");
        var loader = new ExtensionLoader(TestExtensions.Options(store));
        loader.Discover(worktree, "repo-1");

        ExtensionTrustDeniedException exception =
            await Assert.ThrowsAsync<ExtensionTrustDeniedException>(() =>
                loader.Load(
                    "hello",
                    worktree,
                    "repo-1",
                    new FakeExtensionTrustPrompt(approve: false),
                    TestContext.Current.CancellationToken
                )
            );

        Assert.Contains("hello", exception.Message);
        Assert.False(File.Exists(Path.Combine(store, "trust.json")));
    }

    [Fact]
    public async Task Global_extensions_are_not_gated()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        TestExtensions.InstallHelloExtension(temp, "hello");
        var loader = new ExtensionLoader(TestExtensions.Options(store));
        loader.Discover(temp.Root, "repo-1");
        var prompt = new FakeExtensionTrustPrompt(approve: false);

        LoadedExtension loaded = await loader.Load(
            "hello",
            temp.Root,
            "repo-1",
            prompt,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("hello", loaded.Id);
        Assert.Equal(0, prompt.Calls);
        Assert.False(File.Exists(Path.Combine(store, "trust.json")));
    }

    [Fact]
    public void Content_hash_is_stable_content_sensitive_and_order_independent()
    {
        using var temp = new TempDirectory();
        string first = temp.Subdirectory("first");
        string second = temp.Subdirectory("second");
        File.WriteAllText(Path.Combine(first, "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(first, "b.txt"), "beta");
        File.WriteAllText(Path.Combine(second, "b.txt"), "beta");
        File.WriteAllText(Path.Combine(second, "a.txt"), "alpha");

        string hash = ExtensionTrustStore.ComputeContentHash(first);

        Assert.Equal(hash, ExtensionTrustStore.ComputeContentHash(first));
        Assert.Equal(hash, ExtensionTrustStore.ComputeContentHash(second));
        File.WriteAllText(Path.Combine(first, "a.txt"), "changed");
        Assert.NotEqual(hash, ExtensionTrustStore.ComputeContentHash(first));
    }

    [Fact]
    public void Evaluate_returns_the_decision_for_each_trust_state()
    {
        using var temp = new TempDirectory();
        var store = new ExtensionTrustStore(TestExtensions.Options(temp.Subdirectory("store")));

        Assert.Equal(ExtensionTrustDecision.Prompt, store.Evaluate("repo-1", "/work", "hash-1"));

        store.RecordTrusted("repo-1", "/work", "hash-1", DateTimeOffset.UnixEpoch);
        Assert.Equal(ExtensionTrustDecision.Trusted, store.Evaluate("repo-1", "/work", "hash-1"));
        Assert.Equal(ExtensionTrustDecision.Prompt, store.Evaluate("repo-1", "/work", "hash-2"));
        Assert.Equal(
            ExtensionTrustDecision.NewWorktree,
            store.Evaluate("repo-1", "/other", "hash-1")
        );
    }
}
