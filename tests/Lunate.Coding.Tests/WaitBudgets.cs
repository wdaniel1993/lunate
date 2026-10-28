namespace Lunate.Coding.Tests;

/// <summary>
/// Wall-clock ceilings for the waits that observe work this process does not schedule for them: the
/// session crossing a turn boundary, a child process dying after the tool killed it.
/// </summary>
/// <remarks>
/// Every leg of the gate runs one solution-wide <c>dotnet test</c> pass, so all the test assemblies
/// start at once on a three- or four-core runner — twenty-odd processes, several of which spawn
/// children of their own. On PR #225's run two unrelated tests in the same assembly (a notice
/// journal wait and a tree-kill wait) missed a 10 s and a 30 s ceiling on the same tick, while the
/// Ubuntu and macOS legs passed at those numbers on the same commit. Neither wait was looking at
/// the wrong thing: the session had done what the test asked, and the process the tool killed was
/// gone. The ceilings were written for an unloaded machine and the runner was not one.
/// <para>
/// The first fix sized the cross-turn wait by platform, on the reading that only windows-latest was
/// starved. PR #231 falsified that reading: macOS missed the same wait at the same 10 s while doing
/// exactly what the test asked. The platform was standing in for "a shared runner", and the two do
/// not line up — macos-latest is the smallest of the three runners, not the roomiest. There is one
/// ceiling now, for every platform, because the wait it covers is one wait: a run writing its
/// session and its history, the follow-up queue draining, the next turn starting, all of it queuing
/// behind whatever else the runner happens to be doing.
/// </para>
/// <para>
/// A ceiling costs nothing when the machine is fast — every poll exits as soon as its condition
/// holds — and a genuine hang still fails the leg, only later. Widening these is not
/// rerun-until-green: the number now matches the machine the gate runs on.
/// </para>
/// </remarks>
internal static class WaitBudgets
{
    /// <summary>
    /// A wait that spans a turn boundary: the run finishes (the session and its history are
    /// written), the follow-up queue drains, and the next turn starts. The writes alone queue behind
    /// the rest of the runner, on every leg and by more than the tight in-process window allows.
    /// </summary>
    public static readonly TimeSpan CrossTurn = TimeSpan.FromSeconds(60);

    /// <summary>
    /// A spawned child reporting its own pid, which every tree-kill test in <c>Lunate.Coding.Tests</c>
    /// waits for before it kills anything. The file is the child's first statement, so the wait is
    /// really for the child to start: on a loaded runner that is a shell cold start (Windows
    /// PowerShell is the slowest of them) queued behind everything else the suite is spawning.
    /// </summary>
    public static readonly TimeSpan ChildReport = TimeSpan.FromSeconds(60);

    /// <summary>
    /// A child process the tool has just killed, observed dead. This one stays split by platform,
    /// because the split is about teardown and not about load: a Windows tree kill is handed to the
    /// OS and settles behind whatever the runner is spawning, while a POSIX kill is immediate and no
    /// Unix leg has missed thirty seconds for one.
    /// </summary>
    public static readonly TimeSpan ProcessExit = OperatingSystem.IsWindows()
        ? TimeSpan.FromSeconds(60)
        : TimeSpan.FromSeconds(30);
}
