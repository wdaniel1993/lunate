using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class FileChangeBusTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_notification_carries_the_canonical_path_and_the_workspace_id()
    {
        using var bus = new FileChangeBus("/work");
        var handler = new RecordingFileChangedHandler();
        bus.Subscribe(handler);

        bus.Notify("/work/a.txt");
        await bus.DrainAsync(Ct);

        FileChangedPayload payload = Assert.Single(handler.Payloads);
        Assert.Equal("/work/a.txt", payload.Path);
        Assert.Equal("/work", payload.WorkspaceId);
    }

    [Fact]
    public async Task Handlers_run_by_priority_descending_with_registration_order_on_ties()
    {
        using var bus = new FileChangeBus("/w");
        List<string> order = [];
        bus.Subscribe(Handler(0, () => order.Add("first-low")));
        bus.Subscribe(Handler(10, () => order.Add("high")));
        bus.Subscribe(Handler(0, () => order.Add("second-low")));

        bus.Notify("/w/a.txt");
        await bus.DrainAsync(Ct);

        Assert.Equal(["high", "first-low", "second-low"], order);
    }

    [Fact]
    public async Task Unsubscribing_stops_delivery()
    {
        using var bus = new FileChangeBus("/w");
        var handler = new RecordingFileChangedHandler();
        IDisposable subscription = bus.Subscribe(handler);

        subscription.Dispose();
        bus.Notify("/w/a.txt");
        await bus.DrainAsync(Ct);

        Assert.Empty(handler.Payloads);
    }

    [Theory]
    [InlineData(null, "/w/src/b.cs", true)]
    [InlineData("", "/w/src/b.cs", true)]
    [InlineData("*.cs", "/w/src/b.cs", true)]
    [InlineData("*.cs", "/w/a.txt", false)]
    [InlineData("*/src/*.cs", "/w/src/b.cs", true)]
    [InlineData("*/src/*.cs", "/w/other/b.cs", false)]
    [InlineData("*/?.cs", "/w/a.cs", true)]
    [InlineData("*/?.cs", "/w/ab.cs", false)]
    [InlineData("*/a?.cs", "/w/ab.cs", true)]
    [InlineData("*/a?.cs", "/w/a.cs", false)]
    public async Task The_pattern_filter_matches_star_and_question(
        string? pattern,
        string path,
        bool expected
    )
    {
        using var bus = new FileChangeBus("/w");
        var handler = new RecordingFileChangedHandler();
        bus.Subscribe(handler, pattern);

        bus.Notify(path);
        await bus.DrainAsync(Ct);

        Assert.Equal(expected ? 1 : 0, handler.Payloads.Count);
    }

    [Fact]
    public async Task Pattern_case_follows_the_platform()
    {
        using var bus = new FileChangeBus("/w");
        var handler = new RecordingFileChangedHandler();
        bus.Subscribe(handler, "*.TXT");

        bus.Notify("/w/a.txt");
        await bus.DrainAsync(Ct);

        Assert.Equal(OperatingSystem.IsLinux() ? 0 : 1, handler.Payloads.Count);
    }

    [Fact]
    public async Task A_throwing_handler_is_reported_and_delivery_continues()
    {
        var log = new RecordingExtensionLog();
        using var bus = new FileChangeBus("/w", log);
        bus.Subscribe(
            new TestFileChangedHandler(
                0,
                (_, _) => throw new InvalidOperationException("handler exploded")
            )
        );
        var survivor = new RecordingFileChangedHandler();
        bus.Subscribe(survivor);

        try
        {
            bus.Notify("/w/a.txt");
        }
        catch (Exception exception)
        {
            Assert.Fail($"Notify threw: {exception}");
        }

        await bus.DrainAsync(Ct);

        Assert.Single(survivor.Payloads);
        Assert.Contains(
            log.Messages,
            message => message.Contains("failed", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_hanging_handler_is_cut_off_and_reported()
    {
        var log = new RecordingExtensionLog();
        var options = new HookRunnerOptions { HandlerTimeout = TimeSpan.FromMilliseconds(50) };
        using var bus = new FileChangeBus("/w", log, options);
        bus.Subscribe(
            new TestFileChangedHandler(0, async (_, ct) => await Task.Delay(Timeout.Infinite, ct))
        );
        var survivor = new RecordingFileChangedHandler();
        bus.Subscribe(survivor);

        bus.Notify("/w/a.txt");
        await bus.DrainAsync(Ct);

        Assert.Single(survivor.Payloads);
        Assert.Contains(
            log.Messages,
            message => message.Contains("timed out", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Events_follow_the_queued_mutation_order_under_concurrency()
    {
        using var bus = new FileChangeBus("/w");
        List<string> delivered = [];
        bus.Subscribe(
            new TestFileChangedHandler(
                0,
                (payload, _) =>
                {
                    lock (delivered)
                    {
                        delivered.Add(payload.Path);
                    }

                    return ValueTask.CompletedTask;
                }
            )
        );
        var queue = new FileMutationQueue();
        string queuedPath = Path.Combine(Path.GetTempPath(), $"lunate-bus-{Guid.NewGuid():N}.txt");
        List<string> applied = [];

        Task[] mutations =
        [
            .. Enumerable
                .Range(1, 8)
                .Select(index =>
                    queue.RunAsync(
                        queuedPath,
                        _ =>
                        {
                            lock (applied)
                            {
                                applied.Add($"/w/f-{index}");
                            }

                            bus.Notify($"/w/f-{index}");
                            return Task.FromResult(0);
                        },
                        Ct
                    )
                ),
        ];
        await Task.WhenAll(mutations);
        await bus.DrainAsync(Ct);

        Assert.Equal(applied, delivered);
    }

    private static TestFileChangedHandler Handler(int priority, Action action) =>
        new(
            priority,
            (_, _) =>
            {
                action();
                return ValueTask.CompletedTask;
            }
        );
}
