using S6.Harness;

namespace S6.Tests;

internal sealed class FakeClock
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;

    public DateTimeOffset Read() => Now;

    public void Advance(TimeSpan delta) => Now += delta;
}

/// <summary>Owns a headless live loop on a background task for one test.</summary>
internal sealed class LiveSession : IAsyncDisposable
{
    private readonly XenoLiveApp _app;
    private readonly Task _run;

    private LiveSession(XenoLiveApp app)
    {
        _app = app;
        _run = Task.Run(() => app.RunAsync());
    }

    public XenoLiveApp App => _app;

    /// <summary>
    /// Stops the live loop. One-shot renders open a fresh global Terminal
    /// instance (there is no public isolated instance), so they must not run
    /// while the loop is live.
    /// </summary>
    public async Task StopAsync()
    {
        _app.RequestStop();
        await _run;
    }

    public static async Task<LiveSession> StartAsync(FakeClock? clock = null)
    {
        var session = new LiveSession(
            XenoLiveApp.CreateHeadless(
                Scenario.InitialWidth,
                Scenario.InitialHeight,
                clock is null ? null : clock.Read
            )
        );
        await session.App.WaitForTicksAsync(1, TimeSpan.FromSeconds(10));
        return session;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _app.Dispose();
    }
}
