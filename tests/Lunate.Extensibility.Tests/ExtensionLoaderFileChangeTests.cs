namespace Lunate.Extensibility.Tests;

public sealed class ExtensionLoaderFileChangeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Subscribed_extensions_receive_bus_events_and_unload_ends_the_subscription()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallHelloExtension(temp, "hello");
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");
        using var bus = new FileChangeBus(Path.GetFullPath(temp.Root), log);
        loader.Services.RegisterCore("core/file-bus", bus);
        await loader.Load("hello", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        bus.Notify("/work/a.txt");
        await bus.DrainAsync(Ct);

        Assert.Contains(
            log.Messages,
            message => message.Contains("hello file changed: /work/a.txt", StringComparison.Ordinal)
        );
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("hello lookups: bus=True, service=True", StringComparison.Ordinal)
        );

        await loader.Unload("hello");
        bus.Notify("/work/b.txt");
        await bus.DrainAsync(Ct);

        Assert.DoesNotContain(
            log.Messages,
            message => message.Contains("hello file changed: /work/b.txt", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Subscribing_without_a_registered_bus_is_reported()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallHelloExtension(temp, "hello");
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");

        await loader.Load("hello", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        Assert.Contains(
            log.Messages,
            message => message.Contains("no core/file-bus", StringComparison.Ordinal)
        );
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains(
                    "hello lookups: bus=False, service=True",
                    StringComparison.Ordinal
                )
        );
    }
}
