using S6.Harness;

namespace S6.Tests;

/// <summary>
/// The S-5 shared behaviours driven through the real XenoAtom.Terminal.UI
/// inline host loop against <c>InMemoryTerminalBackend</c>: scriptable input
/// and captured output, no real terminal. Wall-clock timings stay loose; only
/// the Ctrl+C window uses the injected clock.
/// </summary>
public sealed class LiveLoopTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Scenario_ReplaysAllBehaviours_AndQuits()
    {
        using var app = XenoLiveApp.CreateHeadless(Scenario.InitialWidth, Scenario.InitialHeight);
        var result = await ScenarioReplay.RunAsync(
            app,
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.True(result.QuitRequested);
        Assert.True(result.CancelRequested);
        Assert.Contains(result.FinishedBlocks, b => b.Contains("48 passed", StringComparison.Ordinal));
        Assert.Contains(
            "queued: also check the tests",
            result.ScreenText,
            StringComparison.Ordinal
        );
        Assert.Contains("input cleared", result.Status, StringComparison.Ordinal);
        Assert.Equal(869, result.TailText.Length);
    }

    [Fact]
    public async Task StreamingTail_ShowsAccumulatedText()
    {
        await using var session = await LiveSession.StartAsync();
        var app = session.App;
        app.PostEvent(new AgentEvent.RunStarted(Scenario.Model));
        app.PostEvent(new AgentEvent.TextDelta("hello "));
        app.PostEvent(new AgentEvent.TextDelta("world"));
        await app.WaitForAsync(
            () => app.Model.TailText.Contains("hello world", StringComparison.Ordinal),
            Wait
        );

        await session.StopAsync();
        var screen = app.RenderLiveSnapshot(80, 24);
        Assert.Contains("hello world", screen, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Footer_ShowsModelTokensAndContext()
    {
        await using var session = await LiveSession.StartAsync();
        var app = session.App;
        app.PostEvent(new AgentEvent.RunStarted(Scenario.Model));
        app.PostEvent(new AgentEvent.Usage(1200, 340, 12.5));
        await app.WaitForAsync(() => app.Model.OutputTokens == 340, Wait);

        await session.StopAsync();
        var screen = app.RenderLiveSnapshot(80, 24);
        Assert.Contains(Scenario.Model, screen, StringComparison.Ordinal);
        Assert.Contains("1540 tok", screen, StringComparison.Ordinal);
        Assert.Contains("12.5% ctx", screen, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approval_YesRunsTheTool()
    {
        await using var session = await LiveSession.StartAsync();
        var app = session.App;
        app.PostEvent(new AgentEvent.RunStarted(Scenario.Model));
        app.PostEvent(new AgentEvent.ApprovalRequested("bash", "dotnet test"));
        await app.WaitForAsync(() => app.Model.PendingApprovalText is not null, Wait);
        Assert.Equal("bash: dotnet test", app.Model.PendingApprovalText);
        Assert.Equal("approve bash: dotnet test? [y]es [n]o [a]lways", app.Model.ApprovalPrompt);

        app.SendKey(Keys.Letter('y'));
        await app.WaitForAsync(() => app.Model.PendingApprovalText is null, Wait);
        Assert.Equal("approved bash", app.Model.Status);
    }

    [Fact]
    public async Task Approval_NoDeniesTheTool()
    {
        await using var session = await LiveSession.StartAsync();
        var app = session.App;
        app.PostEvent(new AgentEvent.RunStarted(Scenario.Model));
        app.PostEvent(new AgentEvent.ApprovalRequested("bash", "dotnet test"));
        await app.WaitForAsync(() => app.Model.PendingApprovalText is not null, Wait);

        app.SendKey(Keys.Letter('n'));
        await app.WaitForAsync(() => app.Model.PendingApprovalText is null, Wait);
        Assert.Equal("denied bash", app.Model.Status);
    }

    [Fact]
    public async Task Approval_AlwaysIsCachedForTheSession()
    {
        await using var session = await LiveSession.StartAsync();
        var app = session.App;
        app.PostEvent(new AgentEvent.RunStarted(Scenario.Model));
        app.PostEvent(new AgentEvent.ApprovalRequested("bash", "dotnet test"));
        await app.WaitForAsync(() => app.Model.PendingApprovalText is not null, Wait);

        app.SendKey(Keys.Letter('a'));
        await app.WaitForAsync(() => app.Model.AlwaysApproved.Contains("bash"), Wait);

        app.PostEvent(new AgentEvent.ApprovalRequested("bash", "dotnet format"));
        await app.WaitForAsync(
            () => app.Model.Status == "auto-approved bash (always)",
            Wait
        );
        Assert.Null(app.Model.PendingApprovalText);
    }

    [Fact]
    public async Task Steering_TypedWhileRunning_IsQueuedAndShown()
    {
        await using var session = await LiveSession.StartAsync();
        var app = session.App;
        app.PostEvent(new AgentEvent.RunStarted(Scenario.Model));
        await app.WaitForAsync(() => app.Model.Running, Wait);

        foreach (var c in "check tests")
        {
            app.SendKey(c == ' ' ? Keys.Space : Keys.Letter(c));
        }

        app.SendKey(Keys.Enter);
        await app.WaitForAsync(() => app.Model.Steering.Count == 1, Wait);

        Assert.Equal("check tests", app.Model.Steering[0]);
        await session.StopAsync();
        Assert.Contains(
            "queued: check tests",
            app.RenderLiveSnapshot(80, 24),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Escape_CancelsTheRun()
    {
        await using var session = await LiveSession.StartAsync();
        var app = session.App;
        app.PostEvent(new AgentEvent.RunStarted(Scenario.Model));
        app.PostEvent(new AgentEvent.ToolStarted("bash"));
        await app.WaitForAsync(() => app.Model.ToolRunning, Wait);

        app.SendKey(Keys.Escape);
        await app.WaitForAsync(() => app.Model.CancelRequested, Wait);

        Assert.False(app.Model.Running);
        Assert.False(app.Model.ToolRunning);
        Assert.Null(app.Model.SpinnerGlyph);
    }

    [Fact]
    public async Task CtrlC_ClearsInput_WithoutQuitting()
    {
        var clock = new FakeClock();
        await using var session = await LiveSession.StartAsync(clock);
        var app = session.App;
        app.PostEvent(new AgentEvent.RunStarted(Scenario.Model));
        await app.WaitForAsync(() => app.Model.Running, Wait);

        app.SendKey(Keys.CtrlC);
        await app.WaitForAsync(() => app.Model.Status?.StartsWith("input cleared") == true, Wait);

        Assert.False(app.Model.QuitRequested);
        Assert.NotNull(app.Model.LastCtrlC);
    }

    [Fact]
    public async Task CtrlC_TwiceInsideTheWindow_Quits()
    {
        var clock = new FakeClock();
        await using var session = await LiveSession.StartAsync(clock);
        var app = session.App;

        app.SendKey(Keys.CtrlC);
        await app.WaitForAsync(() => app.Model.LastCtrlC is not null, Wait);

        clock.Advance(TimeSpan.FromSeconds(1.9));
        app.SendKey(Keys.CtrlC);
        await app.WaitForAsync(() => app.Model.QuitRequested, Wait);
    }

    [Fact]
    public async Task CtrlC_TwiceOutsideTheWindow_DoesNotQuit()
    {
        var clock = new FakeClock();
        await using var session = await LiveSession.StartAsync(clock);
        var app = session.App;

        app.SendKey(Keys.CtrlC);
        await app.WaitForAsync(() => app.Model.LastCtrlC is not null, Wait);

        clock.Advance(TimeSpan.FromSeconds(2.1));
        app.SendKey(Keys.CtrlC);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.False(app.Model.QuitRequested);
    }

    [Fact]
    public async Task Resize_ReflowsTheLiveArea()
    {
        await using var session = await LiveSession.StartAsync();
        var app = session.App;
        app.PostEvent(new AgentEvent.RunStarted(Scenario.Model));
        app.PostEvent(new AgentEvent.TextDelta(new string('x', 200)));
        await app.WaitForAsync(() => app.Model.TailText.Length >= 200, Wait);

        app.Resize(40, 24);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await session.StopAsync();

        var screen = app.RenderLiveSnapshot(40, 24);
        Assert.All(screen.Split('\n'), line => Assert.True(line.Length <= 40, line));
    }

    [Fact]
    public async Task FinishedBlock_IsWrittenAboveTheLiveRegion()
    {
        await using var session = await LiveSession.StartAsync();
        var app = session.App;
        app.PostEvent(new AgentEvent.RunStarted(Scenario.Model));
        app.PostEvent(new AgentEvent.ToolFinished("bash", true, "48 passed"));
        await app.WaitForAsync(() => app.FinishedBlocks.Count == 1, Wait);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal("**bash** ok: 48 passed", app.FinishedBlocks[0]);
        Assert.Contains("bash", app.GetOutputText(), StringComparison.Ordinal);
        Assert.Contains("48 passed", app.GetOutputText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BurstOfTenThousandDeltas_LosesNothing_AndCancelIsClean()
    {
        await using var session = await LiveSession.StartAsync();
        var app = session.App;
        app.PostEvent(new AgentEvent.RunStarted(Scenario.Model));

        var expected = new System.Text.StringBuilder();
        for (var i = 0; i < 10_000; i++)
        {
            var token = $"{i};";
            expected.Append(token);
            app.PostEvent(new AgentEvent.TextDelta(token));
        }

        await app.WaitForAsync(
            () => app.Model.TailText.Length == expected.Length,
            TimeSpan.FromSeconds(30)
        );
        Assert.Equal(expected.ToString(), app.Model.TailText);

        app.SendKey(Keys.Escape);
        await app.WaitForAsync(() => app.Model.CancelRequested, Wait);
        Assert.False(app.Model.Running);
        Assert.False(app.Model.ToolRunning);
        Assert.Null(app.Model.PendingApprovalText);
    }
}
