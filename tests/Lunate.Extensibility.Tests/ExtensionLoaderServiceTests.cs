using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class ExtensionLoaderServiceTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Registered_services_start_with_the_session_and_stop_when_it_ends()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallHelloExtension(temp, "hello");
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");

        await loader.Load("hello", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        Assert.Single(
            log.Messages,
            message => message.Contains("hello service started", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            log.Messages,
            message => message.Contains("hello service stopped", StringComparison.Ordinal)
        );

        await loader.EndSessionAsync(Ct);
        await loader.EndSessionAsync(Ct);

        Assert.Single(
            log.Messages,
            message => message.Contains("hello service stopped", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Unloading_stops_the_extensions_services_and_a_reload_starts_a_fresh_one()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallHelloExtension(temp, "hello");
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");
        var prompt = new FakeExtensionTrustPrompt(true);
        await loader.Load("hello", temp.Root, "repo", prompt, Ct);

        await loader.Unload("hello");

        Assert.Single(
            log.Messages,
            message => message.Contains("hello service stopped", StringComparison.Ordinal)
        );

        await loader.Load("hello", temp.Root, "repo", prompt, Ct);

        Assert.Equal(
            2,
            log.Messages.Count(message =>
                message.Contains("hello service started", StringComparison.Ordinal)
            )
        );
    }

    [Fact]
    public async Task Services_do_not_start_after_the_session_ended()
    {
        using var temp = new TempDirectory();
        TestExtensions.InstallHelloExtension(temp, "hello");
        var log = new RecordingExtensionLog();
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store"), log));
        loader.Discover(temp.Root, "repo");
        await loader.Load("hello", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);
        await loader.EndSessionAsync(Ct);
        await loader.Unload("hello");

        await loader.Load("hello", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        Assert.Single(
            log.Messages,
            message => message.Contains("hello service started", StringComparison.Ordinal)
        );
    }
}
