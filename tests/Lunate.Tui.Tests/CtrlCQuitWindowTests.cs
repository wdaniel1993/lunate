using Microsoft.Reactive.Testing;

namespace Lunate.Tui.Tests;

public sealed class CtrlCQuitWindowTests
{
    [Fact]
    public void Hint_names_the_second_press()
    {
        Assert.Equal("press Ctrl+C again to quit", CtrlCQuitWindow.Hint);
    }

    [Fact]
    public void Non_empty_input_clears_and_disarms()
    {
        var scheduler = new TestScheduler();
        var window = new CtrlCQuitWindow(scheduler);

        Assert.Equal(CtrlCAction.ClearInput, window.Press(inputEmpty: false));
        Assert.False(window.IsArmed);

        Assert.Equal(CtrlCAction.Arm, window.Press(inputEmpty: true));
        Assert.True(window.IsArmed);

        Assert.Equal(CtrlCAction.ClearInput, window.Press(inputEmpty: false));
        Assert.False(window.IsArmed);

        Assert.Equal(CtrlCAction.Arm, window.Press(inputEmpty: true));
        Assert.True(window.IsArmed);
    }

    [Fact]
    public void First_empty_press_arms()
    {
        var scheduler = new TestScheduler();
        var window = new CtrlCQuitWindow(scheduler);

        Assert.Equal(CtrlCAction.Arm, window.Press(inputEmpty: true));
        Assert.True(window.IsArmed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1999)]
    [InlineData(2000)]
    public void Second_empty_press_inside_the_window_quits(int elapsedMilliseconds)
    {
        var scheduler = new TestScheduler();
        var window = new CtrlCQuitWindow(scheduler);
        window.Press(inputEmpty: true);

        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(elapsedMilliseconds).Ticks);

        Assert.Equal(CtrlCAction.Quit, window.Press(inputEmpty: true));
        Assert.False(window.IsArmed);
    }

    [Fact]
    public void Expired_window_re_arms_and_the_next_press_restarts_the_window()
    {
        var scheduler = new TestScheduler();
        var window = new CtrlCQuitWindow(scheduler);
        window.Press(inputEmpty: true);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(2001).Ticks);

        Assert.Equal(CtrlCAction.ReArm, window.Press(inputEmpty: true));
        Assert.True(window.IsArmed);

        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(2000).Ticks);
        Assert.Equal(CtrlCAction.Quit, window.Press(inputEmpty: true));
    }

    [Fact]
    public void Custom_window_length_is_honoured()
    {
        var scheduler = new TestScheduler();
        var window = new CtrlCQuitWindow(scheduler, TimeSpan.FromMilliseconds(500));
        window.Press(inputEmpty: true);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        Assert.Equal(CtrlCAction.Quit, window.Press(inputEmpty: true));
    }

    [Fact]
    public void There_is_no_cancel_action()
    {
        Assert.Equal(
            new[] { "ClearInput", "Arm", "Quit", "ReArm" }.Order(StringComparer.Ordinal),
            Enum.GetNames<CtrlCAction>().Order(StringComparer.Ordinal)
        );
    }
}
