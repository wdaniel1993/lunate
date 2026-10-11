using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class McpServerRegistryTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public void First_registration_per_name_wins_and_a_duplicate_is_refused_and_logged_naming_both()
    {
        var log = new RecordingExtensionLog();
        var registry = new McpServerRegistry(log);
        McpServerRegistration first = Registration("test", "first-command");
        McpServerRegistration duplicate = Registration("test", "second-command");

        registry.Register("first", first);
        registry.Register("second", duplicate);

        RegisteredMcpServer registered = Assert.Single(registry.List());
        Assert.Equal("first", registered.ExtensionId);
        Assert.Same(first, registered.Registration);
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("'test'", StringComparison.Ordinal)
                && message.Contains("'first'", StringComparison.Ordinal)
                && message.Contains("'second'", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Unregister_drops_only_the_extensions_servers()
    {
        var registry = new McpServerRegistry();
        registry.Register("first", Registration("one"));
        registry.Register("second", Registration("two"));

        registry.Unregister("first");

        RegisteredMcpServer remaining = Assert.Single(registry.List());
        Assert.Equal("second", remaining.ExtensionId);
    }

    [Fact]
    public void Registering_performs_no_io_and_logs_nothing_even_for_a_command_that_cannot_exist()
    {
        var log = new RecordingExtensionLog();
        var registry = new McpServerRegistry(log);

        registry.Register("missing", Registration("test", "/nonexistent/lunate-mcp-command"));

        Assert.Single(registry.List());
        Assert.Empty(log.Messages);
    }

    [Fact]
    public async Task Loader_collects_the_registration_at_load_and_drops_it_on_unload()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallMcpExtension(temp, "mcp");
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");

        await loader.Load("mcp", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        RegisteredMcpServer registered = Assert.Single(loader.McpServers.List());
        Assert.Equal("mcp", registered.ExtensionId);
        Assert.Equal("test", registered.Name);

        await loader.Unload("mcp");

        Assert.Empty(loader.McpServers.List());
    }

    [Fact]
    public async Task Loading_a_registration_with_a_missing_command_starts_nothing_and_logs_nothing()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallMcpExtension(temp, "mcp");
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");

        await loader.Load("mcp", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        Assert.Single(loader.McpServers.List());
        Assert.Empty(log.Messages);
    }

    [Fact]
    public async Task Duplicate_server_names_across_extensions_are_refused_and_logged_naming_both()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallMcpExtension(temp, "first");
        TestExtensions.InstallMcpExtension(temp, "second");
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");
        await loader.Load("first", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        await loader.Load("second", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        RegisteredMcpServer registered = Assert.Single(loader.McpServers.List());
        Assert.Equal("first", registered.ExtensionId);
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("'test'", StringComparison.Ordinal)
                && message.Contains("'first'", StringComparison.Ordinal)
                && message.Contains("'second'", StringComparison.Ordinal)
        );
    }

    private static McpServerRegistration Registration(string name, string command = "command") =>
        new(name, new McpServerConfig(command, []));
}
