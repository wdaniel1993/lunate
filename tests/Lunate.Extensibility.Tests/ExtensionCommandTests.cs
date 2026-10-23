using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The manifest command rules at load: a provided and declared command registers its descriptors,
/// a declared command with no provider is recorded as not runnable, a provided-but-undeclared
/// command refuses the load, a built-in collision refuses the load naming both, and unload removes
/// the source.
/// </summary>
public sealed class ExtensionCommandTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_declared_and_provided_command_registers_its_descriptors()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallCommandExtension(
            temp,
            "cmd",
            """["ping"]""",
            """{"commands":[{"name":"ping","description":"Ping it","argumentHint":"<text>"}]}"""
        );
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));
        loader.Discover(temp.Root, "repo");

        await loader.Load("cmd", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        RegisteredCommandSource registered = Assert.Single(loader.CommandSources.List());
        Assert.Equal("cmd", registered.ExtensionId);
        CommandDescriptor descriptor = Assert.Single(registered.Descriptors);
        Assert.Equal("ping", descriptor.Name);
        Assert.Equal("Ping it", descriptor.Description);
        Assert.Equal("<text>", descriptor.ArgumentHint);
        Assert.Equal("cmd", descriptor.SourceId);
        Assert.Equal(CommandKind.Extension, descriptor.Kind);
        Assert.Empty(loader.DeclaredNotProvidedCommands("cmd"));
    }

    [Fact]
    public async Task A_declared_command_without_a_provider_is_recorded_as_not_runnable()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallCommandExtension(temp, "cmd", """["ghost"]""");
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");

        await loader.Load("cmd", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        Assert.Empty(loader.CommandSources.List());
        Assert.Equal(["ghost"], loader.DeclaredNotProvidedCommands("cmd"));
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("declares command '/ghost'", StringComparison.Ordinal)
                && message.Contains("not runnable", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_provided_but_undeclared_command_refuses_the_load()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallCommandExtension(
            temp,
            "cmd",
            "[]",
            """{"commands":[{"name":"surprise","description":"Not declared"}]}"""
        );
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));
        loader.Discover(temp.Root, "repo");

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(() =>
            loader.Load("cmd", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct)
        );

        Assert.Contains("extension 'cmd'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'/surprise'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not declared", exception.Message, StringComparison.Ordinal);
        Assert.Contains("manifest", exception.Message, StringComparison.Ordinal);
        Assert.Empty(loader.CommandSources.List());
    }

    [Fact]
    public async Task A_built_in_name_collision_refuses_the_load_naming_both()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallCommandExtension(
            temp,
            "cmd",
            """["help"]""",
            """{"commands":[{"name":"help","description":"Extension help"}]}"""
        );
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(
            new ExtensionHostOptions
            {
                StorePath = temp.Subdirectory("store"),
                Log = log,
                ReservedCommands = ["help"],
            }
        );
        loader.Discover(temp.Root, "repo");

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(() =>
            loader.Load("cmd", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct)
        );

        Assert.Contains("extension 'cmd'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'/help'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("built-in", exception.Message, StringComparison.Ordinal);
        Assert.Empty(loader.CommandSources.List());
    }

    [Fact]
    public async Task Unload_removes_the_extension_command_sources()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallCommandExtension(
            temp,
            "cmd",
            """["ping"]""",
            """{"commands":[{"name":"ping","description":"Ping it"}]}"""
        );
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));
        loader.Discover(temp.Root, "repo");
        await loader.Load("cmd", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        await loader.Unload("cmd");

        Assert.Empty(loader.CommandSources.List());
    }

    [Fact]
    public async Task Two_sources_of_one_extension_providing_one_command_log_the_command_id()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallCommandExtension(
            temp,
            "cmd",
            """["ping"]""",
            """{"commands":[{"name":"ping","description":"Ping it"}],"sources":2}"""
        );
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");

        await loader.Load("cmd", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("'ext:cmd:ping'", StringComparison.Ordinal)
                && message.Contains("more than one source", StringComparison.Ordinal)
        );
        // The load-time stamping still yields the extension kind and the extension id as source,
        // on both registrations.
        IReadOnlyList<RegisteredCommandSource> registered = loader.CommandSources.List();
        Assert.Equal(2, registered.Count);
        Assert.All(
            registered,
            source =>
            {
                CommandDescriptor descriptor = Assert.Single(source.Descriptors);
                Assert.Equal(CommandKind.Extension, descriptor.Kind);
                Assert.Equal("cmd", descriptor.SourceId);
            }
        );
    }
}
