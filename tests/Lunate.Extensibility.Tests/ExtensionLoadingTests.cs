using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class ExtensionLoadingTests
{
    [Fact]
    public async Task Load_creates_the_extension_and_runs_the_factory()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallHelloExtension(temp, "hello");
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");

        LoadedExtension loaded = await loader.Load(
            "hello",
            temp.Root,
            "repo",
            new FakeExtensionTrustPrompt(approve: true),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("hello", loaded.Id);
        Assert.NotNull(loaded.Extension);
        Assert.Equal("hello", loaded.Descriptor.Id);
        Assert.Contains(
            log.Messages,
            message => message.Contains("created hello instance 1", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Unload_is_idempotent_and_a_second_load_is_fresh()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallHelloExtension(temp, "hello");
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");
        var prompt = new FakeExtensionTrustPrompt(approve: true);

        await loader.Load(
            "hello",
            temp.Root,
            "repo",
            prompt,
            TestContext.Current.CancellationToken
        );
        await loader.Unload("hello");
        await loader.Unload("hello");
        await loader.Load(
            "hello",
            temp.Root,
            "repo",
            prompt,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            2,
            log.Messages.Count(message =>
                message.Contains("created hello instance 1", StringComparison.Ordinal)
            )
        );
        Assert.DoesNotContain(
            log.Messages,
            message => message.Contains("instance 2", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_missing_entry_assembly_fails_actionably()
    {
        using var temp = new TempDirectory();
        string directory = TestExtensions.GlobalExtensionDirectory(temp, "hello");
        TestExtensions.WriteManifest(directory, "hello", entryAssembly: "Missing.dll");
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));
        loader.Discover(temp.Root, "repo");

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(() =>
            loader.Load(
                "hello",
                temp.Root,
                "repo",
                new FakeExtensionTrustPrompt(approve: true),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("hello", exception.Message);
        Assert.Contains("Missing.dll", exception.Message);
        Assert.Contains(directory, exception.Message);
    }

    [Fact]
    public async Task A_missing_dependency_fails_actionably()
    {
        using var temp = new TempDirectory();
        string directory = TestExtensions.GlobalExtensionDirectory(temp, "hello");
        TestExtensions.CopyFixture(directory, "HelloExtension.dll");
        TestExtensions.WriteManifest(directory, "hello");
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));
        loader.Discover(temp.Root, "repo");

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(() =>
            loader.Load(
                "hello",
                temp.Root,
                "repo",
                new FakeExtensionTrustPrompt(approve: true),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("hello", exception.Message);
        Assert.Contains("HelloExtension.Support", exception.Message);
    }

    [Fact]
    public async Task An_entry_assembly_without_a_factory_fails_actionably()
    {
        using var temp = new TempDirectory();
        string directory = TestExtensions.GlobalExtensionDirectory(temp, "empty");
        TestExtensions.CopyFixture(directory, "Lunate.Extensibility.Abstractions.dll");
        TestExtensions.WriteManifest(
            directory,
            "empty",
            entryAssembly: "Lunate.Extensibility.Abstractions.dll"
        );
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));
        loader.Discover(temp.Root, "repo");

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(() =>
            loader.Load(
                "empty",
                temp.Root,
                "repo",
                new FakeExtensionTrustPrompt(approve: true),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("empty", exception.Message);
        Assert.Contains("IExtensionFactory", exception.Message);
    }

    [Fact]
    public async Task An_entry_assembly_with_several_factories_fails_actionably()
    {
        using var temp = new TempDirectory();
        string directory = TestExtensions.GlobalExtensionDirectory(temp, "many");
        TestExtensions.CopyFixture(
            directory,
            "HelloExtension.Support.dll",
            "Lunate.Extensibility.Abstractions.dll"
        );
        TestExtensions.WriteManifest(
            directory,
            "many",
            entryAssembly: "HelloExtension.Support.dll"
        );
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));
        loader.Discover(temp.Root, "repo");

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(() =>
            loader.Load(
                "many",
                temp.Root,
                "repo",
                new FakeExtensionTrustPrompt(approve: true),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("many", exception.Message);
        Assert.Contains("2", exception.Message);
        Assert.Contains("IExtensionFactory", exception.Message);
    }

    [Fact]
    public async Task Shared_assemblies_ignore_same_named_files_in_the_extension_directory()
    {
        using var temp = new TempDirectory();
        string directory = TestExtensions.GlobalExtensionDirectory(temp, "hello");
        TestExtensions.CopyFixture(directory, "HelloExtension.dll", "HelloExtension.Support.dll");
        File.Copy(
            TestExtensions.FixtureFile("HelloExtension.Support.dll"),
            Path.Combine(directory, "System.Text.Json.dll"),
            overwrite: true
        );
        File.Copy(
            TestExtensions.FixtureFile("HelloExtension.Support.dll"),
            Path.Combine(directory, "Microsoft.Extensions.AI.Abstractions.dll"),
            overwrite: true
        );
        TestExtensions.WriteManifest(directory, "hello");
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");

        await loader.Load(
            "hello",
            temp.Root,
            "repo",
            new FakeExtensionTrustPrompt(approve: true),
            TestContext.Current.CancellationToken
        );

        Assert.Contains(
            log.Messages,
            message => message.Contains("shared \"hello\"/ChatMessage", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Loading_an_undiscovered_id_fails_actionably()
    {
        using var temp = new TempDirectory();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));
        loader.Discover(temp.Root, "repo");

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(() =>
            loader.Load(
                "ghost",
                temp.Root,
                "repo",
                new FakeExtensionTrustPrompt(approve: true),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("ghost", exception.Message);
    }

    [Fact]
    public async Task Loading_a_loaded_extension_again_fails_actionably()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallHelloExtension(temp, "hello");
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));
        loader.Discover(temp.Root, "repo");
        var prompt = new FakeExtensionTrustPrompt(approve: true);
        await loader.Load(
            "hello",
            temp.Root,
            "repo",
            prompt,
            TestContext.Current.CancellationToken
        );

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(() =>
            loader.Load("hello", temp.Root, "repo", prompt, TestContext.Current.CancellationToken)
        );

        Assert.Contains("already loaded", exception.Message);
    }

    [Fact]
    public void Default_options_point_at_the_lunate_store()
    {
        ExtensionHostOptions options = ExtensionHostOptions.Default;

        Assert.EndsWith(Path.Combine(".lunate"), options.StorePath, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(options.StorePath, "extensions"), options.ExtensionsPath);
    }
}
