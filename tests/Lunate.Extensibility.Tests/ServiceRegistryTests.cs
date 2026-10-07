using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class ServiceRegistryTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public void A_registered_service_is_looked_up_by_name()
    {
        var registry = new ServiceRegistry();
        var service = new BackgroundServiceHostTests.RecordingService();

        registry.Register("a", "store", service);

        Assert.True(registry.TryGet("store", out IBackgroundService? found));
        Assert.Same(service, found);
    }

    [Fact]
    public void A_namespaced_name_is_allowed()
    {
        var registry = new ServiceRegistry();

        registry.Register("a", "ext/a/store", new BackgroundServiceHostTests.RecordingService());

        Assert.True(registry.TryGet("ext/a/store", out _));
    }

    [Fact]
    public void Duplicate_names_across_extensions_are_refused_naming_both()
    {
        var registry = new ServiceRegistry();
        registry.Register("a", "store", new BackgroundServiceHostTests.RecordingService());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register("b", "store", new BackgroundServiceHostTests.RecordingService())
        );

        Assert.Contains("'store'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'a'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'b'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reserved_core_name_cannot_be_shadowed()
    {
        var registry = new ServiceRegistry();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register(
                "a",
                "core/file-bus",
                new BackgroundServiceHostTests.RecordingService()
            )
        );

        Assert.Contains("reserved", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'core/file-bus'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'a'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Bad Name")]
    [InlineData("Store")]
    [InlineData("ext/only")]
    [InlineData("ext/hello/bad_name")]
    [InlineData("ext/Hello/store")]
    [InlineData("other/hello/store")]
    public void Malformed_names_are_refused(string name)
    {
        var registry = new ServiceRegistry();

        Assert.Throws<ArgumentException>(() =>
            registry.Register("a", name, new BackgroundServiceHostTests.RecordingService())
        );
    }

    [Fact]
    public void Unknown_names_are_not_found()
    {
        var registry = new ServiceRegistry();

        Assert.False(registry.TryGet("missing", out IBackgroundService? service));
        Assert.Null(service);
    }

    [Fact]
    public void Unregister_removes_only_that_extensions_services()
    {
        var registry = new ServiceRegistry();
        registry.Register("a", "first", new BackgroundServiceHostTests.RecordingService());
        registry.Register("b", "second", new BackgroundServiceHostTests.RecordingService());

        registry.Unregister("a");

        Assert.False(registry.TryGet("first", out _));
        Assert.True(registry.TryGet("second", out _));
    }

    [Fact]
    public async Task The_core_services_are_reachable_as_typed_handles()
    {
        var registry = new ServiceRegistry();
        using var bus = new FileChangeBus("/work");
        registry.RegisterCore("core/file-bus", bus);
        registry.RegisterCore(
            "core/mutation-queue",
            new MutationQueueHandle(new FileMutationQueue())
        );
        registry.RegisterCore("core/workspace", new WorkspaceInfo("/work", "/repo", "/repo/.git"));

        Assert.True(
            registry.TryGetCore<IFileChangeBus>("core/file-bus", out IFileChangeBus? found)
        );
        Assert.Same(bus, found);
        Assert.False(registry.TryGetCore<IMutationQueue>("core/file-bus", out _));

        Assert.True(
            registry.TryGetCore<IMutationQueue>("core/mutation-queue", out IMutationQueue? queue)
        );
        int result = await queue!.RunAsync("/tmp/x", _ => Task.FromResult(42), Ct);
        Assert.Equal(42, result);

        Assert.True(
            registry.TryGetCore<WorkspaceInfo>("core/workspace", out WorkspaceInfo? workspace)
        );
        Assert.Equal("/work", workspace!.WorktreeRoot);
        Assert.Equal("/repo", workspace.RepoRoot);
        Assert.Equal("/repo/.git", workspace.GitCommonDir);
    }

    [Fact]
    public void Core_registration_refuses_duplicates_and_non_core_names()
    {
        var registry = new ServiceRegistry();
        registry.RegisterCore("core/file-bus", new object());

        Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterCore("core/file-bus", new object())
        );
        Assert.Throws<ArgumentException>(() => registry.RegisterCore("service", new object()));
    }
}
