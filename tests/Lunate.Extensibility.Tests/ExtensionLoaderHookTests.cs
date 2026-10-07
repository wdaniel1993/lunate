using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class ExtensionLoaderHookTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Session_started_fires_once_and_session_ending_is_idempotent()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallHelloExtension(temp, "hello");
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");

        await loader.EndSessionAsync(Ct);
        Assert.DoesNotContain(
            log.Messages,
            message => message.Contains("hello session ended", StringComparison.Ordinal)
        );

        await loader.Load("hello", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        Assert.Single(
            log.Messages,
            message => message.Contains("hello session started", StringComparison.Ordinal)
        );

        await loader.EndSessionAsync(Ct);
        await loader.EndSessionAsync(Ct);

        Assert.Single(
            log.Messages,
            message => message.Contains("hello session ended", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Session_started_does_not_repeat_after_an_unload_and_reload()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallHelloExtension(temp, "hello");
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");

        await loader.Load("hello", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);
        await loader.Unload("hello");
        await loader.Load("hello", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        Assert.Single(
            log.Messages,
            message => message.Contains("hello session started", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Registered_handlers_reach_the_runner_and_unload_removes_them()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallHelloExtension(temp, "hello");
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");
        await loader.Load("hello", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        await loader.Hooks.RunSessionStartedAsync(new SessionStartedPayload(temp.Root, "repo"), Ct);
        Assert.Equal(2, Started(log));

        await loader.Unload("hello");
        await loader.Hooks.RunSessionStartedAsync(new SessionStartedPayload(temp.Root, "repo"), Ct);
        Assert.Equal(2, Started(log));

        static int Started(RecordingExtensionLog log) =>
            log.Messages.Count(message =>
                message.Contains("hello session started", StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task A_global_allow_handler_approved_the_project_extension_without_prompting()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        TestExtensions.InstallHelloExtension(temp, "authorizer");
        TestExtensions.InstallProjectHelloExtension(temp.Subdirectory("worktree"), "target");
        var loader = new ExtensionLoader(TestExtensions.Options(store));
        loader.Discover(temp.Subdirectory("worktree"), "repo-1");
        await loader.Load(
            "authorizer",
            temp.Subdirectory("worktree"),
            "repo-1",
            new FakeExtensionTrustPrompt(true),
            Ct
        );
        loader.Hooks.Register("authorizer", Allow());
        var prompt = new FakeExtensionTrustPrompt(true);

        LoadedExtension loaded = await loader.Load(
            "target",
            temp.Subdirectory("worktree"),
            "repo-1",
            prompt,
            Ct
        );

        Assert.Equal("target", loaded.Id);
        Assert.Equal(0, prompt.Calls);
        Assert.True(File.Exists(Path.Combine(store, "trust.json")));

        static TestProjectTrustHandler Allow() =>
            new((_, _) => ValueTask.FromResult<ProjectTrustResult>(new ProjectTrustResult.Allow()));
    }

    [Fact]
    public async Task A_global_deny_handler_refuses_the_project_extension_without_prompting()
    {
        using var temp = new TempDirectory();
        string worktree = temp.Subdirectory("worktree");
        TestExtensions.InstallHelloExtension(temp, "authorizer");
        TestExtensions.InstallProjectHelloExtension(worktree, "target");
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));
        loader.Discover(worktree, "repo-1");
        await loader.Load("authorizer", worktree, "repo-1", new FakeExtensionTrustPrompt(true), Ct);
        loader.Hooks.Register(
            "authorizer",
            new TestProjectTrustHandler(
                (_, _) =>
                    ValueTask.FromResult<ProjectTrustResult>(new ProjectTrustResult.Deny("no"))
            )
        );
        var prompt = new FakeExtensionTrustPrompt(true);

        await Assert.ThrowsAsync<ExtensionTrustDeniedException>(() =>
            loader.Load("target", worktree, "repo-1", prompt, Ct)
        );

        Assert.Equal(0, prompt.Calls);
    }

    [Fact]
    public async Task A_failing_global_trust_handler_denies_fail_safe()
    {
        using var temp = new TempDirectory();
        string worktree = temp.Subdirectory("worktree");
        TestExtensions.InstallHelloExtension(temp, "authorizer");
        TestExtensions.InstallProjectHelloExtension(worktree, "target");
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));
        loader.Discover(worktree, "repo-1");
        await loader.Load("authorizer", worktree, "repo-1", new FakeExtensionTrustPrompt(true), Ct);
        loader.Hooks.Register(
            "authorizer",
            new TestProjectTrustHandler(
                (_, _) => throw new InvalidOperationException("trust backend down")
            )
        );
        var prompt = new FakeExtensionTrustPrompt(true);

        await Assert.ThrowsAsync<ExtensionTrustDeniedException>(() =>
            loader.Load("target", worktree, "repo-1", prompt, Ct)
        );

        Assert.Equal(0, prompt.Calls);
    }

    [Fact]
    public async Task Project_extension_handlers_do_not_participate_in_the_trust_flow()
    {
        using var temp = new TempDirectory();
        string worktree = temp.Subdirectory("worktree");
        TestExtensions.InstallProjectHelloExtension(worktree, "first");
        TestExtensions.InstallProjectHelloExtension(worktree, "second");
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));
        loader.Discover(worktree, "repo-1");
        await loader.Load("first", worktree, "repo-1", new FakeExtensionTrustPrompt(true), Ct);
        loader.Hooks.Register(
            "first",
            new TestProjectTrustHandler(
                (_, _) =>
                    ValueTask.FromResult<ProjectTrustResult>(new ProjectTrustResult.Deny("no"))
            )
        );
        var prompt = new FakeExtensionTrustPrompt(true);

        LoadedExtension loaded = await loader.Load("second", worktree, "repo-1", prompt, Ct);

        Assert.Equal("second", loaded.Id);
        Assert.Equal(1, prompt.Calls);
    }
}
