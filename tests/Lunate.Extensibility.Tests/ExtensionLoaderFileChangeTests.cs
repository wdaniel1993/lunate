using Lunate.Agent;

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
        loader.Services.RegisterCore(
            "core/mutation-queue",
            new MutationQueueHandle(FileMutationQueue.Shared)
        );
        await loader.Load("hello", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        bus.Notify(
            Changes.Event("/work/a.cs", "a.cs", "/work", "run_test", FileChangeKind.Created)
        );
        await WaitForLineAsync(
            log,
            "hello file changed: path=/work/a.cs, relative=a.cs, workspace=/work, run=run_test, kind=created",
            Ct
        );

        Assert.Contains(
            log.Messages,
            message =>
                message.Contains(
                    "hello lookups: bus=True, queue=True, service=True",
                    StringComparison.Ordinal
                )
        );

        await loader.Unload("hello");
        bus.Notify(Changes.Event("/work/b.cs"));
        await bus.DrainAsync(Ct);

        Assert.DoesNotContain(
            log.Messages,
            message =>
                message.Contains("hello file changed: path=/work/b.cs", StringComparison.Ordinal)
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
                    "hello lookups: bus=False, queue=False, service=True",
                    StringComparison.Ordinal
                )
        );
    }

    private static async Task WaitForLineAsync(
        RecordingExtensionLog log,
        string text,
        CancellationToken ct
    )
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!log.Messages.Any(message => message.Contains(text, StringComparison.Ordinal)))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"the log never contained '{text}'; log:\n{string.Join("\n", log.Messages)}"
                );
            }

            await Task.Delay(10, ct);
        }
    }
}
