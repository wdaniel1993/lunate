using S5.Harness;

namespace S5.Tests;

/// <summary>
/// The shared test list. Every variant runs the identical scenarios; the only
/// difference is the fixture that wires the concurrency model. Time-based tests
/// go through <see cref="ISession.Advance"/> (virtual time for all variants).
/// </summary>
public abstract class LiveAreaTests
{
    protected abstract IVariantFixture CreateFixture();

    [Fact]
    public async Task Scenario_ReplaysAllBehaviours_AndQuits()
    {
        using var session = await StartAsync();
        var result = ScenarioRunner.Run(session);

        Assert.True(result.QuitRequested);
        Assert.True(result.CancelRequested);
        Assert.Contains(result.Scrollback, block => block.Contains("bash ok: 48 passed", StringComparison.Ordinal));
        Assert.Contains(
            session.Terminal.Frames,
            frame => frame.Lines.Any(line => line.Contains("run cancelled (user)", StringComparison.Ordinal)));
        Assert.Contains("queued: also check the tests", string.Join('\n', result.LiveArea), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamingTail_RendersAccumulatedText()
    {
        using var session = await StartAsync();
        session.Post(new AgentEvent.RunStarted(Scenario.Model));
        session.Post(new AgentEvent.TextDelta("hello "));
        session.Post(new AgentEvent.TextDelta("world"));

        await AdvanceFrames(session, 1);

        Assert.Contains("hello world", session.Snapshot()[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TailRedraw_IsCappedAtThirtyFps()
    {
        using var session = await StartAsync();
        for (var i = 0; i < 300; i++)
        {
            session.Post(new AgentEvent.TextDelta($"{i} "));
        }

        await AdvanceFrames(session, 1);
        Assert.Single(session.Terminal.Frames);

        await AdvanceFrames(session, 10);
        Assert.Single(session.Terminal.Frames);

        session.Post(new AgentEvent.TextDelta("again"));
        await AdvanceFrames(session, 1);
        Assert.Equal(2, session.Terminal.Frames.Count);
    }

    [Fact]
    public async Task TailRedraw_UnderSustainedDeltas_StaysNearThirtyFps()
    {
        using var session = await StartAsync();
        var elapsed = TimeSpan.Zero;
        for (var i = 0; i < 30; i++)
        {
            session.Post(new AgentEvent.TextDelta($"{i} "));
            await AdvanceAsync(session, TimeSpan.FromMilliseconds(10));
            elapsed += TimeSpan.FromMilliseconds(10);
        }

        var cap = (int)(elapsed / Scenario.FrameInterval) + 2;
        Assert.InRange(session.Terminal.Frames.Count, 1, cap);
        Assert.Contains("29 ", session.TailText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Spinner_AnimatesOnlyWhileToolRuns()
    {
        using var session = await StartAsync();
        session.Post(new AgentEvent.RunStarted(Scenario.Model));
        session.Post(new AgentEvent.ToolStarted("bash"));

        var glyphs = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            await AdvanceFrames(session, 1);
            glyphs.Add(session.Terminal.Frames[^1].Lines[^3]);
        }

        Assert.True(glyphs.Select(g => g[0]).Distinct().Count() >= 3, string.Join(" | ", glyphs));

        session.Post(new AgentEvent.ToolFinished("bash", Ok: true, "done"));
        await AdvanceFrames(session, 2);
        Assert.DoesNotContain(session.Terminal.Frames[^1].Lines, Spinner.IsGlyphLine);
    }

    [Fact]
    public async Task Footer_ShowsModelTokensAndContext()
    {
        using var session = await StartAsync();
        session.Post(new AgentEvent.RunStarted(Scenario.Model));
        session.Post(new AgentEvent.Usage(1200, 340, 12.5));
        await AdvanceFrames(session, 1);

        var live = string.Join('\n', session.Snapshot());
        Assert.Contains(Scenario.Model, live, StringComparison.Ordinal);
        Assert.Contains("1540 tok", live, StringComparison.Ordinal);
        Assert.Contains("12.5% ctx", live, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approval_YesRunsTheTool()
    {
        using var session = await StartAsync();
        session.Post(new AgentEvent.RunStarted(Scenario.Model));
        session.Post(new AgentEvent.ApprovalRequested("bash", "dotnet test"));
        await AdvanceFrames(session, 1);
        Assert.True(session.IsApprovalPending);
        Assert.Contains("approve bash: dotnet test?", session.Terminal.LiveText, StringComparison.Ordinal);

        session.Key(Keys.Letter('y'));
        await AdvanceFrames(session, 1);

        Assert.False(session.IsApprovalPending);
        Assert.Contains("approved bash", session.Terminal.LiveText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approval_NoDeniesTheTool()
    {
        using var session = await StartAsync();
        session.Post(new AgentEvent.RunStarted(Scenario.Model));
        session.Post(new AgentEvent.ApprovalRequested("bash", "dotnet test"));
        session.Key(Keys.Letter('n'));
        await AdvanceFrames(session, 1);

        Assert.False(session.IsApprovalPending);
        Assert.Contains("denied bash", session.Terminal.LiveText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approval_AlwaysIsCachedForTheSession()
    {
        using var session = await StartAsync();
        session.Post(new AgentEvent.RunStarted(Scenario.Model));
        session.Post(new AgentEvent.ApprovalRequested("bash", "dotnet test"));
        session.Key(Keys.Letter('a'));
        await AdvanceFrames(session, 1);

        session.Post(new AgentEvent.ApprovalRequested("bash", "dotnet format"));
        await AdvanceFrames(session, 1);

        Assert.False(session.IsApprovalPending);
        Assert.Contains("auto-approved bash (always)", session.Terminal.LiveText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Steering_TypedWhileRunning_IsQueuedAndShown()
    {
        using var session = await StartAsync();
        session.Post(new AgentEvent.RunStarted(Scenario.Model));
        await AdvanceFrames(session, 1);

        foreach (var c in "check tests")
        {
            session.Key(c == ' ' ? Keys.Space : Keys.Letter(c));
        }

        session.Key(Keys.Enter);
        await AdvanceFrames(session, 1);

        Assert.Contains("queued: check tests", session.Terminal.LiveText, StringComparison.Ordinal);
        Assert.EndsWith("> ", session.Terminal.LiveArea[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Escape_CancelsTheRun()
    {
        using var session = await StartAsync();
        session.Post(new AgentEvent.RunStarted(Scenario.Model));
        session.Post(new AgentEvent.ToolStarted("bash"));
        await AdvanceFrames(session, 1);
        Assert.True(session.IsToolRunning);

        session.Key(Keys.Escape);
        await AdvanceFrames(session, 2);

        Assert.False(session.IsRunning);
        Assert.False(session.IsToolRunning);
        Assert.True(session.IsCancelRequested);
        Assert.DoesNotContain(session.Terminal.Frames[^1].Lines, Spinner.IsGlyphLine);
    }

    [Fact]
    public async Task CtrlC_ClearsInput_WithoutQuitting()
    {
        using var session = await StartAsync();
        foreach (var c in "abc")
        {
            session.Key(Keys.Letter(c));
        }

        await AdvanceFrames(session, 1);
        Assert.EndsWith("> abc", session.Terminal.LiveArea[^1], StringComparison.Ordinal);

        session.Key(Keys.CtrlC);
        await AdvanceFrames(session, 1);

        Assert.False(session.IsQuitRequested);
        Assert.EndsWith("> ", session.Terminal.LiveArea[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CtrlC_TwiceWithinTwoSeconds_Quits()
    {
        using var session = await StartAsync();
        session.Key(Keys.CtrlC);
        await AdvanceAsync(session, TimeSpan.FromSeconds(1.9));
        session.Key(Keys.CtrlC);
        await AdvanceFrames(session, 1);

        Assert.True(session.IsQuitRequested);
    }

    [Fact]
    public async Task CtrlC_TwiceOutsideTheWindow_DoesNotQuit()
    {
        using var session = await StartAsync();
        session.Key(Keys.CtrlC);
        await AdvanceAsync(session, TimeSpan.FromSeconds(2.1));
        session.Key(Keys.CtrlC);
        await AdvanceFrames(session, 1);

        Assert.False(session.IsQuitRequested);
    }

    [Fact]
    public async Task Resize_ReflowsTheLiveArea()
    {
        using var session = await StartAsync();
        session.Post(new AgentEvent.RunStarted(Scenario.Model));
        session.Post(new AgentEvent.TextDelta(new string('x', 200)));
        await AdvanceFrames(session, 1);

        session.Resize(40, 24);
        await AdvanceFrames(session, 1);

        Assert.All(session.Terminal.LiveArea, line => Assert.True(line.Length <= 40, line));
    }

    [Fact]
    public async Task BurstOfTenThousandDeltas_LosesNothing_AndCancelIsClean()
    {
        using var session = await StartAsync();
        session.Post(new AgentEvent.RunStarted(Scenario.Model));

        var expected = new System.Text.StringBuilder();
        for (var i = 0; i < 10_000; i++)
        {
            var token = $"{i};";
            expected.Append(token);
            session.Post(new AgentEvent.TextDelta(token));
        }

        await AdvanceFrames(session, 3);
        Assert.Equal(expected.ToString(), session.TailText);

        session.Key(Keys.Escape);
        await AdvanceFrames(session, 3);

        Assert.False(session.IsRunning);
        Assert.False(session.IsToolRunning);
        Assert.False(session.IsApprovalPending);
        Assert.True(session.IsCancelRequested);

        session.Dispose();
        session.Dispose();
    }

    private Task<ISession> StartAsync()
    {
        var session = CreateFixture().CreateSession();
        return StartCoreAsync(session);
    }

    private static async Task<ISession> StartCoreAsync(ISession session)
    {
        await session.DrainAsync();
        return session;
    }

    private static async Task AdvanceAsync(ISession session, TimeSpan delta)
    {
        session.Advance(delta);
        await session.DrainAsync();
    }

    private static Task AdvanceFrames(ISession session, int frames) =>
        AdvanceAsync(session, Scenario.FrameInterval * frames);
}

public sealed class VariantATests : LiveAreaTests
{
    protected override IVariantFixture CreateFixture() => new VariantAFixture();
}

public sealed class VariantBTests : LiveAreaTests
{
    protected override IVariantFixture CreateFixture() => new VariantBFixture();
}

public sealed class VariantBPlusTests : LiveAreaTests
{
    protected override IVariantFixture CreateFixture() => new VariantBPlusFixture();
}
