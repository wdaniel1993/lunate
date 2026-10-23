using Microsoft.Reactive.Testing;

namespace Lunate.Tui.Tests;

public sealed class QuitWindowTests
{
    [Fact]
    public void Hints_name_the_second_press()
    {
        Assert.Equal("press Ctrl+C again to quit", QuitWindow.Hint);
        Assert.Equal("press Ctrl+D again to quit", QuitWindow.ExitHint);
    }

    [Fact]
    public void Non_empty_input_clears_and_disarms()
    {
        var scheduler = new TestScheduler();
        QuitWindow window = QuitWindow.ForCtrlC(scheduler);

        Assert.Equal(QuitAction.ClearInput, window.Press(inputEmpty: false));
        Assert.False(window.IsArmed);

        Assert.Equal(QuitAction.Arm, window.Press(inputEmpty: true));
        Assert.True(window.IsArmed);

        Assert.Equal(QuitAction.ClearInput, window.Press(inputEmpty: false));
        Assert.False(window.IsArmed);

        Assert.Equal(QuitAction.Arm, window.Press(inputEmpty: true));
        Assert.True(window.IsArmed);
    }

    [Fact]
    public void First_empty_press_arms()
    {
        var scheduler = new TestScheduler();
        QuitWindow window = QuitWindow.ForCtrlC(scheduler);

        Assert.Equal(QuitAction.Arm, window.Press(inputEmpty: true));
        Assert.True(window.IsArmed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1999)]
    [InlineData(2000)]
    public void Second_empty_press_inside_the_window_quits(int elapsedMilliseconds)
    {
        var scheduler = new TestScheduler();
        QuitWindow window = QuitWindow.ForCtrlC(scheduler);
        window.Press(inputEmpty: true);

        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(elapsedMilliseconds).Ticks);

        Assert.Equal(QuitAction.Quit, window.Press(inputEmpty: true));
        Assert.False(window.IsArmed);
    }

    [Fact]
    public void Expired_window_re_arms_and_the_next_press_restarts_the_window()
    {
        var scheduler = new TestScheduler();
        QuitWindow window = QuitWindow.ForCtrlC(scheduler);
        window.Press(inputEmpty: true);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(2001).Ticks);

        Assert.Equal(QuitAction.ReArm, window.Press(inputEmpty: true));
        Assert.True(window.IsArmed);

        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(2000).Ticks);
        Assert.Equal(QuitAction.Quit, window.Press(inputEmpty: true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(799)]
    [InlineData(800)]
    public void Ctrl_D_quits_on_a_second_press_within_800ms(int elapsedMilliseconds)
    {
        var scheduler = new TestScheduler();
        QuitWindow window = QuitWindow.ForExit(scheduler);
        Assert.Equal(QuitAction.Arm, window.PressWhen(eligible: true));

        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(elapsedMilliseconds).Ticks);

        Assert.Equal(QuitAction.Quit, window.PressWhen(eligible: true));
        Assert.False(window.IsArmed);
    }

    [Fact]
    public void Ctrl_D_outside_the_window_re_arms_instead_of_quitting()
    {
        var scheduler = new TestScheduler();
        QuitWindow window = QuitWindow.ForExit(scheduler);
        window.PressWhen(eligible: true);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(801).Ticks);

        Assert.Equal(QuitAction.ReArm, window.PressWhen(eligible: true));
        Assert.True(window.IsArmed);
    }

    [Fact]
    public void Ctrl_D_when_ineligible_ignores_the_press_and_disarms()
    {
        var scheduler = new TestScheduler();
        QuitWindow window = QuitWindow.ForExit(scheduler);
        window.PressWhen(eligible: true);

        Assert.Equal(QuitAction.Ignore, window.PressWhen(eligible: false));
        Assert.False(window.IsArmed);

        Assert.Equal(QuitAction.Arm, window.PressWhen(eligible: true));
    }

    [Fact]
    public void The_actions_are_the_expected_set()
    {
        Assert.Equal(
            new[] { "Arm", "ClearInput", "Ignore", "Quit", "ReArm" }.Order(StringComparer.Ordinal),
            Enum.GetNames<QuitAction>().Order(StringComparer.Ordinal)
        );
    }
}
