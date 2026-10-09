using S6.Harness;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;

namespace S6.Tests;

/// <summary>
/// Tests that need deterministic frames and virtual time. They drive the
/// retained visual tree through the reflected internal tick hook; if the
/// library moves those hooks, these tests are the early warning.
/// </summary>
public sealed class DeterministicRenderTests
{
    [Fact]
    public void TickDriver_ReachesTheLiveVisual_AndCapturesOutput()
    {
        var model = new LiveModel();
        var visual = new LiveVisual(model, () => DateTimeOffset.UnixEpoch);
        using var driver = new TickDriver(visual);
        driver.Tick(2);

        Assert.False(string.IsNullOrEmpty(driver.Backend.GetOutText()));
    }

    [Fact]
    public void StreamingTail_RendersAccumulatedText()
    {
        var model = new LiveModel();
        model.ApplyEvent(new AgentEvent.RunStarted(Scenario.Model));
        model.ApplyEvent(new AgentEvent.TextDelta("hello "));
        model.ApplyEvent(new AgentEvent.TextDelta("world"));

        var visual = new LiveVisual(model, () => DateTimeOffset.UnixEpoch);
        visual.Refresh();
        using var driver = new TickDriver(visual);
        driver.Tick(2);

        var screen = new AnsiScreen(80, 24);
        screen.Apply(driver.Backend.GetOutText());
        Assert.Contains("hello world", screen.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Footer_ShowsModelTokensAndContext_Invariant()
    {
        var model = new LiveModel();
        model.ApplyEvent(new AgentEvent.RunStarted(Scenario.Model));
        model.ApplyEvent(new AgentEvent.Usage(1200, 340, 12.5));

        var visual = new LiveVisual(model, () => DateTimeOffset.UnixEpoch);
        visual.Refresh();
        using var driver = new TickDriver(visual);
        driver.Tick(2);

        var screen = new AnsiScreen(80, 24);
        screen.Apply(driver.Backend.GetOutText());
        var live = screen.GetText();
        Assert.True(
            live.Contains(Scenario.Model, StringComparison.Ordinal),
            $"raw={driver.Backend.GetOutText().Replace("\x1b", "\\e")} screen=[{live}]"
        );
        Assert.Contains("1540 tok", live, StringComparison.Ordinal);
        Assert.Contains("12.5% ctx", live, StringComparison.Ordinal);
    }

    [Fact]
    public void ApprovalPrompt_ShowsToolAndArguments()
    {
        var model = new LiveModel();
        model.ApplyEvent(new AgentEvent.RunStarted(Scenario.Model));
        model.ApplyEvent(new AgentEvent.ApprovalRequested("bash", "dotnet test"));

        var visual = new LiveVisual(model, () => DateTimeOffset.UnixEpoch);
        visual.Refresh();
        using var driver = new TickDriver(visual);
        driver.Tick(2);

        var screen = new AnsiScreen(80, 24);
        screen.Apply(driver.Backend.GetOutText());
        Assert.Contains("approve bash: dotnet test?", screen.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void TailRedraw_UnderBurst_IsCoalescedByTheHostLoop()
    {
        var model = new LiveModel();
        model.ApplyEvent(new AgentEvent.RunStarted(Scenario.Model));

        var visual = new LiveVisual(model, () => DateTimeOffset.UnixEpoch);
        visual.Refresh();
        using var driver = new TickDriver(visual);
        driver.Tick(2);

        var before = driver.Backend.GetOutText().Length;
        for (var i = 0; i < 300; i++)
        {
            model.ApplyEvent(new AgentEvent.TextDelta($"{i} "));
        }

        visual.Refresh();
        driver.Tick(1);
        var afterFirst = driver.Backend.GetOutText().Length;
        Assert.True(afterFirst > before, "one tick after the burst must repaint once");

        driver.Tick(10);
        var afterIdle = driver.Backend.GetOutText().Length;

        // The framework's diff renderer emits nothing when frames are identical;
        // ten idle ticks must not add ten repaints.
        var growth = afterIdle - afterFirst;
        Assert.True(
            growth < (afterFirst - before) * 10,
            $"idle ticks repainted excessively ({growth} chars over 10 ticks)"
        );
    }

    [Fact]
    public void Spinner_OnlyWhileToolRuns()
    {
        var model = new LiveModel();
        model.ApplyEvent(new AgentEvent.RunStarted(Scenario.Model));
        model.ApplyEvent(new AgentEvent.ToolStarted("bash"));

        var visual = new LiveVisual(model, () => DateTimeOffset.UnixEpoch);
        var glyphs = new List<char>();
        for (var i = 0; i < 4; i++)
        {
            model.TickSpinner(TimeSpan.Zero, out _);
            visual.Refresh();
            Assert.NotNull(model.SpinnerGlyph);
            glyphs.Add(model.SpinnerGlyph![0]);
        }

        Assert.True(glyphs.Distinct().Count() >= 3, string.Join(" | ", glyphs));

        model.ApplyEvent(new AgentEvent.ToolFinished("bash", true, "done"));
        visual.Refresh();
        Assert.Null(model.SpinnerGlyph);
    }
}
