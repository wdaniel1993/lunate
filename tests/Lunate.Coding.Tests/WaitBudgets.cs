namespace Lunate.Coding.Tests;

/// <summary>
/// Wall-clock budgets for the waits that observe work this process does not schedule for them: the
/// session crossing a turn boundary, a child process dying after the tool killed it.
/// </summary>
/// <remarks>
/// The Windows legs of the gate run one solution-wide <c>dotnet test</c> pass, so every test
/// assembly starts at once on a four-core runner — twenty-odd processes, several of which spawn
/// children of their own. On PR #225's run two unrelated tests in the same assembly (a notice
/// journal wait and a tree-kill wait) missed a 10 s and a 30 s budget on the same tick, while the
/// Ubuntu and macOS legs passed at those budgets on the same commit. Neither wait was looking at
/// the wrong thing: the session had done what the test asked, and the process the tool killed was
/// gone. The budgets were written for an unloaded machine and the runner was not one.
/// <para>
/// So the Windows legs get a window a starved runner cannot eat, and the Unix legs keep the tight
/// one, where a slow wait is still a bug worth failing on. A passing wait costs nothing — every
/// poll exits as soon as its condition holds — and a genuine hang still fails the leg, only later.
/// Widening these is not rerun-until-green: the number now matches the machine the gate runs on.
/// </para>
/// </remarks>
internal static class WaitBudgets
{
    /// <summary>
    /// A wait that spans a turn boundary: the run finishes (the session and its history are
    /// written), the follow-up queue drains, and the next turn starts. The writes alone queue
    /// behind the rest of the runner on Windows, so the tight in-process budget does not fit.
    /// </summary>
    public static readonly TimeSpan CrossTurn = OperatingSystem.IsWindows()
        ? TimeSpan.FromSeconds(60)
        : TimeSpan.FromSeconds(10);

    /// <summary>
    /// A child process the tool has just killed, observed dead. A process teardown queues behind
    /// whatever the runner is spawning at that moment; thirty seconds was not enough there.
    /// </summary>
    public static readonly TimeSpan ProcessExit = OperatingSystem.IsWindows()
        ? TimeSpan.FromSeconds(60)
        : TimeSpan.FromSeconds(30);
}
