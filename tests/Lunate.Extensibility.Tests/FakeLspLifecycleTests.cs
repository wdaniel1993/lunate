using FakeLsp;

namespace Lunate.Extensibility.Tests;

public sealed class FakeLspLifecycleTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_registered_service_starts_serves_a_request_and_stops()
    {
        var server = new FakeLspServer();
        var host = new BackgroundServiceHost();
        host.Register("lsp", "ext/lsp/server", new FakeLspService(server));

        await host.StartAsync(Ct);
        string response = await server.RequestAsync("initialize", Ct);
        await host.StopAsync(Ct);

        Assert.Equal("pong:initialize", response);
        Assert.Equal(1, server.StartCount);
        Assert.Equal(1, server.StopCount);
    }

    [Fact]
    public async Task The_service_is_restart_safe()
    {
        var server = new FakeLspServer();
        var host = new BackgroundServiceHost();
        host.Register("lsp", "ext/lsp/server", new FakeLspService(server));

        await host.StartAsync(Ct);
        await host.StartAsync(Ct);
        await host.StopAsync(Ct);
        await host.StopAsync(Ct);
        Assert.Equal(1, server.StartCount);

        await host.StartAsync(Ct);
        string response = await server.RequestAsync("initialized", Ct);
        await host.StopAsync(Ct);

        Assert.Equal("pong:initialized", response);
        Assert.Equal(2, server.StartCount);
        Assert.Equal(2, server.StopCount);
    }

    [Fact]
    public async Task A_failing_lsp_start_is_reported_and_never_thrown()
    {
        var log = new RecordingExtensionLog();
        var server = new FakeLspServer();
        var host = new BackgroundServiceHost(log);
        host.Register("lsp", "ext/lsp/server", new FakeLspService(server, failOnStart: true));

        await host.StartAsync(Ct);

        Assert.Equal(0, server.StartCount);
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("'lsp'", StringComparison.Ordinal)
                && message.Contains("'ext/lsp/server'", StringComparison.Ordinal)
                && message.Contains("failed to start", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_failing_lsp_stop_is_reported_and_never_thrown()
    {
        var log = new RecordingExtensionLog();
        var server = new FakeLspServer();
        var host = new BackgroundServiceHost(log);
        host.Register("lsp", "ext/lsp/server", new FakeLspService(server, failOnStop: true));

        await host.StartAsync(Ct);
        await host.StopAsync(Ct);

        Assert.Equal(1, server.StartCount);
        Assert.Equal(0, server.StopCount);
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("'lsp'", StringComparison.Ordinal)
                && message.Contains("failed to stop", StringComparison.Ordinal)
        );
    }
}
