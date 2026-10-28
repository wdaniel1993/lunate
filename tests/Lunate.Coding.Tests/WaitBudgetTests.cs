namespace Lunate.Coding.Tests;

/// <summary>
/// The budget a harness wait takes when its caller does not name one. It has to be the gate's
/// cross-turn ceiling, not a window written for an idle machine: on PR #231's macOS leg the
/// slow-hook notice wait missed a 10 s window — the inherited default of this helper, because the
/// caller had no way to say that its wait spanned a turn boundary — while the session was doing
/// exactly what the test asked.
/// </summary>
public sealed class WaitBudgetTests
{
    /// <summary>
    /// A wait that names no budget outlives the window every defaulted wait used to have. The
    /// delay is deliberate and is the whole assertion: a condition that turns true after the old
    /// window closed is the one shape that tells an unnamed wait sized for the runner apart from
    /// one sized for a fast machine, and there is no seam to move the clock through. Every wait
    /// here polls, so this passes the moment the delay ends and a genuine hang still fails — just
    /// later than a 10 s default would have reported it.
    /// </summary>
    [Fact]
    public async Task A_wait_that_names_no_budget_outlives_the_old_window()
    {
        using var host = new InteractiveSessionHost();
        Task slow = Task.Delay(TimeSpan.FromSeconds(10.3), TestContext.Current.CancellationToken);

        await host.WaitUntilAsync(() => slow.IsCompleted);
    }
}
