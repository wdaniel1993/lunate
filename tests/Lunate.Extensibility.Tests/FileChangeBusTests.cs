using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class FileChangeBusTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_notification_maps_the_full_event_and_the_wire_kind()
    {
        using var bus = new FileChangeBus("/work");
        var handler = new RecordingFileChangedHandler();
        bus.Subscribe(handler);

        bus.Notify(
            new FileChangeEvent(
                "/work/src/a.cs",
                "src/a.cs",
                "/work",
                "run_1",
                FileChangeKind.Created
            )
        );
        await bus.DrainAsync(Ct);

        FileChangedPayload payload = Assert.Single(handler.Payloads);
        Assert.Equal("/work/src/a.cs", payload.Path);
        Assert.Equal("src/a.cs", payload.RelativePath);
        Assert.Equal("/work", payload.WorkspaceId);
        Assert.Equal("run_1", payload.RunId);
        Assert.Equal("created", payload.Kind);
    }

    [Fact]
    public async Task A_modified_event_maps_the_modified_wire_kind()
    {
        using var bus = new FileChangeBus("/work");
        var handler = new RecordingFileChangedHandler();
        bus.Subscribe(handler);

        bus.Notify(Changes.Event("/work/a.cs", kind: FileChangeKind.Modified));
        await bus.DrainAsync(Ct);

        Assert.Equal("modified", Assert.Single(handler.Payloads).Kind);
    }

    [Fact]
    public async Task The_bus_workspace_id_is_the_fallback_for_unstamped_events()
    {
        using var bus = new FileChangeBus("/work");
        var handler = new RecordingFileChangedHandler();
        bus.Subscribe(handler);

        bus.Notify(
            new FileChangeEvent("/work/a.cs", "a.cs", string.Empty, null, FileChangeKind.Modified)
        );
        await bus.DrainAsync(Ct);

        Assert.Equal("/work", Assert.Single(handler.Payloads).WorkspaceId);
    }

    [Fact]
    public async Task Handlers_run_by_priority_descending_with_registration_order_on_ties()
    {
        using var bus = new FileChangeBus("/w");
        List<string> order = [];
        bus.Subscribe(Handler(0, () => order.Add("first-low")));
        bus.Subscribe(Handler(10, () => order.Add("high")));
        bus.Subscribe(Handler(0, () => order.Add("second-low")));

        bus.Notify(Changes.Event("/w/a.txt"));
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
        bus.Notify(Changes.Event("/w/a.txt"));
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

        bus.Notify(Changes.Event(path));
        await bus.DrainAsync(Ct);

        Assert.Equal(expected ? 1 : 0, handler.Payloads.Count);
    }

    [Fact]
    public async Task Pattern_case_follows_the_platform()
    {
        using var bus = new FileChangeBus("/w");
        var handler = new RecordingFileChangedHandler();
        bus.Subscribe(handler, "*.TXT");

        bus.Notify(Changes.Event("/w/a.txt"));
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
            bus.Notify(Changes.Event("/w/a.txt"));
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

        bus.Notify(Changes.Event("/w/a.txt"));
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

                            bus.Notify(Changes.Event($"/w/f-{index}"));
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

    [Fact]
    public async Task Notifications_after_a_drain_begins_are_dropped_and_logged()
    {
        var log = new RecordingExtensionLog();
        using var bus = new FileChangeBus("/w", log);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bus.Subscribe(
            new TestFileChangedHandler(
                0,
                async (_, _) =>
                {
                    entered.TrySetResult();
                    await release.Task;
                }
            )
        );
        var handler = new RecordingFileChangedHandler();
        bus.Subscribe(handler);

        bus.Notify(Changes.Event("/w/a.txt"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        // DrainAsync flips the bus into drain mode synchronously before it awaits.
        Task drain = bus.DrainAsync(Ct).AsTask();

        try
        {
            bus.Notify(Changes.Event("/w/b.txt"));
        }
        catch (Exception exception)
        {
            Assert.Fail($"Notify after a drain began threw: {exception}");
        }

        release.SetResult();
        await drain.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        FileChangedPayload payload = Assert.Single(handler.Payloads);
        Assert.Equal("/w/a.txt", payload.Path);
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("dropped", StringComparison.Ordinal)
                && message.Contains("/w/b.txt", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_slow_handler_never_stalls_a_producer()
    {
        using var bus = new FileChangeBus("/w");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bus.Subscribe(
            new TestFileChangedHandler(
                0,
                async (_, _) =>
                {
                    entered.TrySetResult();
                    await release.Task;
                }
            )
        );
        var handler = new RecordingFileChangedHandler();
        bus.Subscribe(handler);

        bus.Notify(Changes.Event("/w/a.txt"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        // The pump is inside the slow handler; a second enqueue must return immediately.
        bus.Notify(Changes.Event("/w/b.txt"));
        release.SetResult();
        await bus.DrainAsync(Ct);

        Assert.Equal(["/w/a.txt", "/w/b.txt"], handler.Payloads.Select(payload => payload.Path));
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
