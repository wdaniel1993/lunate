namespace Lunate.Coding.Tests;

/// <summary>
/// Wall-clock ceilings for the waits that observe work this process does not schedule for them: the
/// session crossing a turn boundary, a child process starting or dying, a compile landing, an index
/// being built.
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
/// not line up — macos-latest is the smallest of the three runners, not the roomiest.
/// </para>
/// <para>
/// The rule that keeps it from recurring is this: <b>a ceiling lives here, under the name of the
/// condition it waits for, and a call site carries no number.</b> A literal at a call site is
/// invisible to every other caller waiting on the same condition, so it is sized once, for one
/// machine, and the next starved runner fails on a clock instead of on behaviour. After #231 the two
/// numbers it established were the only ones in this file and the rest never moved; the Windows
/// de-AT pass of #255 then failed <c>SlowHookNoticeTests</c> at 30.49 s against a 30 s frame wait
/// whose runner had spent forty-odd seconds starting the turn behind the rest of the solution.
/// </para>
/// <para>
/// Two rules pick a value. Each budget takes the <em>widest</em> number already in use in this
/// assembly for that same condition, so nothing is invented and no call site is tightened by the
/// move. And a wait is only widened after the failing condition has been named: a test that dies on
/// a ceiling is first asked which condition never became true. Where a wait can be expressed as an
/// event — a notice journal that records the post, a gate the test releases itself — it is; a
/// ceiling is for the waits with no event to stand on.
/// </para>
/// <para>
/// A ceiling costs nothing when the machine is fast — every poll exits as soon as its condition
/// holds — and a genuine hang still fails the leg, only later. Widening these is not
/// rerun-until-green: the number now matches the machine the gate runs on.
/// </para>
/// <para>
/// <see cref="WaitCeilingTests"/> keeps the rule: it scans this project's sources for a duration
/// literal at a call site and fails on one, allow-listing only the numbers that are not ceilings —
/// a simulated clock, an input to the code under test, a performance assertion — each with a reason.
/// </para>
/// </remarks>
internal static class WaitBudgets
{
    /// <summary>
    /// A wait that spans a turn boundary: the run finishes (the session and its history are
    /// written), the follow-up queue drains, and the next turn starts. The writes alone queue behind
    /// the rest of the runner, on every leg and by more than the tight in-process window allows.
    /// <para>
    /// It is also the ceiling for a frame only the session's own turn can paint: the turn has to get
    /// there first, and on the Windows de-AT leg of #255 a first chat call took forty-odd seconds
    /// behind the rest of the solution's process-spawning suites. Waits that go through
    /// <see cref="InteractiveSessionHost.WaitUntilAsync"/> and its siblings name no budget and
    /// inherit this one on purpose — that inheritance is the point.
    /// </para>
    /// </summary>
    public static readonly TimeSpan CrossTurn = TimeSpan.FromSeconds(60);

    /// <summary>
    /// A step of work this process owns and a test itself gates: a <c>TaskCompletionSource</c> a test
    /// releases, a gate the session's flow parks on, a task the test started, a command host
    /// finishing an unload. Nothing outside this process paces it, but the runner's scheduler still
    /// decides when it lands, and a loaded leg starves it for tens of seconds.
    /// </summary>
    public static readonly TimeSpan InProcess = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A child process this test spawns becoming usable: the pid marker written by a grandchild, the
    /// first protocol frame of an ACP or MCP child, the first build of a cold <c>dotnet exec</c>.
    /// The cold start is the whole difficulty — a loaded runner starts a shell or a host in tens of
    /// seconds, and thirty was the number this idiom used before one leg starved past it.
    /// </summary>
    public static readonly TimeSpan SpawnWindow = TimeSpan.FromSeconds(60);

    /// <summary>
    /// A child process observed gone: killed by the tool, or asked to exit (<c>lunate --acp</c> after
    /// stdin closed, an MCP server after its set was disposed).
    /// <para>
    /// This one stays split by platform, because the split is about teardown and not about load: a
    /// Windows tree kill is handed to the OS and settles behind whatever the runner is spawning,
    /// while a POSIX kill is immediate and no Unix leg has missed thirty seconds for one.
    /// </para>
    /// </summary>
    public static readonly TimeSpan ProcessExit = OperatingSystem.IsWindows()
        ? TimeSpan.FromSeconds(60)
        : TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a child's pid file that is there but unusable is tolerated before the test refuses
    /// it: a half-written number and a number released between the write and the read are both fixed
    /// by the next read, and neither is worth a red. This is a tolerance rather than a ceiling — the
    /// refusal it leads to is the answer, so the window stays short instead of waiting a runner out.
    /// </summary>
    public static readonly TimeSpan RefusalGrace = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a test that drives <see cref="ChildProcessProbe"/> at a path no child writes to
    /// waits before the probe reports. There is no spawned process to outlast here: the diagnostic
    /// message is the subject, and the wait only has to cover a few poll intervals so the deadline
    /// is reached on the machine rather than in a fake clock.
    /// </summary>
    public static readonly TimeSpan ProbeExpiry = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// A shell command the tool runs to completion — this is the timeout the test hands the tool, not
    /// a wait the test performs: the tool's own kill must be given room not to fire. Sixty seconds is
    /// the value the process-runner tests already used for exactly this, and a command that finishes
    /// exits the wait, so the ceiling only lengthens a failure.
    /// </summary>
    public static readonly TimeSpan CommandRun = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The timeout that makes the tree kill fire, in the tests about <em>that</em> kill: it has to
    /// outlast a cold PowerShell start on a Windows runner — ten seconds was not enough under load,
    /// so the pid would never be written before the tree died — while staying short enough that the
    /// test spends its time asserting the kill and not waiting for a timeout.
    /// </summary>
    public static readonly TimeSpan TreeKillTimeout = OperatingSystem.IsWindows()
        ? TimeSpan.FromSeconds(30)
        : TimeSpan.FromSeconds(2);

    /// <summary>
    /// A compile landing inside the session: the single-file loop's extension build, whose result the
    /// frame shows. Two Roslyn compiles against a reference pack run inside these tests, and a cold
    /// CI runner needs far more than a frame's five seconds.
    /// </summary>
    public static readonly TimeSpan Compile = TimeSpan.FromSeconds(90);

    /// <summary>
    /// A solution-scope diagnostics call over a real fixture workspace: the backend loads, the
    /// compilation is built and the analyzers run, from a spawned MSBuild on a cold runner. Minutes,
    /// not seconds — the call is the test's subject, and its own cost is what the call pays.
    /// </summary>
    public static readonly TimeSpan Diagnostics = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The lazily built file index publishing: the background walk of the worktree, which on the
    /// large-repository budget test is twenty thousand files. Every cursor-nudge wait in the
    /// autocomplete and path-completion tests watches this same publication, so they share the one
    /// ceiling.
    /// </summary>
    public static readonly TimeSpan IndexReady = TimeSpan.FromSeconds(60);

    /// <summary>
    /// A child process that runs a whole session of its own to completion: the lazy-restore probe,
    /// which re-invokes this assembly in a child so the rest of the suite's real restores cannot make
    /// its assertion depend on test order. It composes, loads extensions and finishes a run — the
    /// work of a test, not the start of one.
    /// </summary>
    public static readonly TimeSpan ChildRun = TimeSpan.FromSeconds(180);

    /// <summary>
    /// A live recording run's exchange landing on the fixture path: the model round trip is real and
    /// the network is the party being waited on. Only the re-recording path (<c>LUNATE_RECORD=1</c>)
    /// reaches it; a replayed run is bounded by <see cref="CrossTurn"/>.
    /// </summary>
    public static readonly TimeSpan Recording = TimeSpan.FromMinutes(5);
}
