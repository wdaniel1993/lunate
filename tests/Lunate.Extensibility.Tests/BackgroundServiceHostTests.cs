using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class BackgroundServiceHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Services_start_once_and_stop_once()
    {
        var host = new BackgroundServiceHost();
        var service = new RecordingService();
        host.Register("ext", "svc", service);

        await host.StartAsync(Ct);
        await host.StartAsync(Ct);
        await host.StopAsync(Ct);
        await host.StopAsync(Ct);

        Assert.Equal(1, service.Starts);
        Assert.Equal(1, service.Stops);
    }

    [Fact]
    public async Task A_service_registered_after_the_first_start_starts_on_the_next_start()
    {
        var host = new BackgroundServiceHost();
        var first = new RecordingService();
        var second = new RecordingService();
        host.Register("ext", "first", first);
        await host.StartAsync(Ct);

        host.Register("late", "second", second);
        await host.StartAsync(Ct);

        Assert.Equal(1, first.Starts);
        Assert.Equal(1, second.Starts);
    }

    [Fact]
    public async Task Stopping_then_starting_restarts_the_services()
    {
        var host = new BackgroundServiceHost();
        var service = new RecordingService();
        host.Register("ext", "svc", service);

        await host.StartAsync(Ct);
        await host.StopAsync(Ct);
        await host.StartAsync(Ct);

        Assert.Equal(2, service.Starts);
        Assert.Equal(1, service.Stops);
    }

    [Fact]
    public async Task A_start_failure_is_reported_and_stops_the_extensions_remaining_services()
    {
        var log = new RecordingExtensionLog();
        var host = new BackgroundServiceHost(log);
        var before = new RecordingService();
        var failing = new RecordingService { FailOnStart = true };
        var after = new RecordingService();
        var other = new RecordingService();
        host.Register("bad", "before", before);
        host.Register("bad", "failing", failing);
        host.Register("bad", "after", after);
        host.Register("good", "other", other);

        await host.StartAsync(Ct);

        Assert.Equal(1, before.Starts);
        Assert.Equal(1, failing.Starts);
        Assert.Equal(0, after.Starts);
        Assert.Equal(1, other.Starts);
        Assert.Equal(1, before.Stops);
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("'bad'", StringComparison.Ordinal)
                && message.Contains("'failing'", StringComparison.Ordinal)
        );

        await host.StartAsync(Ct);
        Assert.Equal(1, before.Starts);
        Assert.Equal(0, after.Starts);
    }

    [Fact]
    public async Task Stop_failures_are_reported_and_never_thrown()
    {
        var log = new RecordingExtensionLog();
        var host = new BackgroundServiceHost(log);
        var throwing = new RecordingService { FailOnStop = true };
        var other = new RecordingService();
        host.Register("a", "throwing", throwing);
        host.Register("a", "other", other);
        await host.StartAsync(Ct);

        await host.StopAsync(Ct);

        Assert.Equal(1, throwing.Stops);
        Assert.Equal(1, other.Stops);
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("'a'", StringComparison.Ordinal)
                && message.Contains("'throwing'", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Dropping_an_extension_stops_and_removes_only_its_services()
    {
        var host = new BackgroundServiceHost();
        var first = new RecordingService();
        var second = new RecordingService();
        host.Register("a", "first", first);
        host.Register("b", "second", second);
        await host.StartAsync(Ct);

        await host.DropAsync("a", Ct);

        Assert.Equal(1, first.Stops);
        Assert.Equal(0, second.Stops);
        Assert.False(host.Services.TryGet("first", out _));
        Assert.True(host.Services.TryGet("second", out _));
    }

    [Fact]
    public async Task Dropping_an_unknown_extension_is_a_no_op()
    {
        var host = new BackgroundServiceHost();

        await host.DropAsync("missing", Ct);

        Assert.False(host.Services.TryGet("nothing", out _));
    }

    internal sealed class RecordingService : IBackgroundService
    {
        public int Starts { get; private set; }

        public int Stops { get; private set; }

        public bool FailOnStart { get; init; }

        public bool FailOnStop { get; init; }

        public ValueTask StartAsync(CancellationToken cancellationToken)
        {
            Starts++;
            return FailOnStart
                ? ValueTask.FromException(new InvalidOperationException("start exploded"))
                : ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            Stops++;
            return FailOnStop
                ? ValueTask.FromException(new InvalidOperationException("stop exploded"))
                : ValueTask.CompletedTask;
        }
    }
}
