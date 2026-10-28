using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Lunate.Coding.Tests;

/// <summary>
/// One running process, named by the pid a spawned child wrote to a file for the test to find.
/// </summary>
/// <remarks>
/// A pid is not a name for a process. Windows hands the number out again once its holder exits, and
/// the suite keeps spawning children while it runs, so "pid 2324" can mean the tree-kill test's
/// grandchild at one moment and a system provider host the next. The creation stamp is what
/// survives: a number that was handed to another process does not carry the stamp of the process
/// that wrote the file.
/// </remarks>
internal readonly record struct ChildProcess(int Pid, string Name, DateTime StartedAt)
{
    /// <summary>pid, image name and creation stamp, in the shape every failure message uses.</summary>
    public string Describe =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"pid {Pid} ({Name}, started {StartedAt:HH:mm:ss.fff})"
        );
}

/// <summary>
/// Watches the grandchild a tree-kill test spawned: the child writes its own pid to a file, this
/// probe pins the process wearing that number <em>while it is still running</em>, and the assertion
/// afterwards asks about that process rather than about the number.
/// </summary>
/// <remarks>
/// Pinning after the kill is not the same thing and was the defect: the tests read the file, killed
/// the tree, and only then asked the pid whether it was gone. A read that late can find the number
/// already reassigned, and pinning <em>that</em> process spends the whole survivor budget on
/// something the test never started — run 40704788358 on the powershell leg failed with
/// "pid 2324 (WmiPrvSE, started 14:28:27.780) was still alive 60 seconds after the tree kill",
/// naming a provider host where the shell that wrote the file should have been. So: an identity is
/// taken before the kill, and it has to be a process the command started.
/// </remarks>
internal static class ChildProcessProbe
{
    /// <summary>
    /// Waits for the pid a spawned child writes to <paramref name="pidFile"/>, and pins the process
    /// it names. <paramref name="commandStarted"/> is when the test started the command that spawns
    /// the child: a process created before that cannot be the child, so the number in the file is
    /// not evidence about the kill and the test says which process it found instead of watching it.
    /// <paramref name="window"/> closes when the command under test is over, because a pid read
    /// after the kill no longer says which process the kill ended.
    /// </summary>
    public static async Task<ChildProcess> AwaitWrittenAsync(
        string pidFile,
        DateTime commandStarted,
        CancellationToken window
    )
    {
        DateTime deadline = DateTime.UtcNow + WaitBudgets.SpawnWindow;
        DateTime? unusableSince = null;
        var unusable = string.Empty;
        while (true)
        {
            if (TryReadFile(pidFile) is { } pid)
            {
                if (TryRead(pid) is { } child)
                {
                    if (SpawnedByCommand(child, commandStarted))
                    {
                        return child;
                    }

                    unusable = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{child.Describe} is not the grandchild this test spawned: it was already running when the command started at {commandStarted:HH:mm:ss.fff}, so the pid written to {Path.GetFileName(pidFile)} was held by a different process before the test read it"
                    );
                }
                else
                {
                    unusable = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{pidFile} names pid {pid}, which no process holds any more: the grandchild exited before the test could pin it, so the pid says nothing about what the kill did"
                    );
                }

                // A half-written file can name a shorter number, and a number can be released
                // between the write and the read; both are fixed by the next read of the file, so
                // the refusal is only reported once it has outlived the writer.
                unusableSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - unusableSince.Value > WaitBudgets.RefusalGrace)
                {
                    Assert.Fail(unusable);
                }
            }
            else
            {
                unusableSince = null;
            }

            if (window.IsCancellationRequested)
            {
                Assert.Fail(
                    $"no pid was pinned from {pidFile}: the command ended before the grandchild reported a pid this test could pin, so the kill happened before the test could name the process it ended"
                );
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"no pid was written to {pidFile} within {WaitBudgets.SpawnWindow.TotalSeconds:0} seconds"
                    )
                );
            }

            try
            {
                await Task.Delay(100, window);
            }
            catch (OperationCanceledException)
            {
                // The loop's own check turns this into the message above.
            }
        }
    }

    /// <summary>
    /// Waits until the pinned process is gone. Alive means a process still holds the pid
    /// <em>and</em> started when ours did: the creation stamp is what makes the question "did
    /// <em>this</em> process die?", so a number that was handed to someone else is not the survivor.
    /// The budget only has to cover the teardown itself (<see cref="WaitBudgets.ProcessExit"/> says
    /// why it is wide there); it no longer covers waiting for a process the test never started.
    /// </summary>
    public static async Task AssertEventuallyDeadAsync(ChildProcess child)
    {
        DateTime deadline = DateTime.UtcNow + WaitBudgets.ProcessExit;
        while (IsStillAlive(child))
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{child.Describe} was still alive {WaitBudgets.ProcessExit.TotalSeconds:0} seconds after the tree kill; it wrote its own pid to the file this test watched while the command was running, so the kill left it behind"
                    )
                );
            }

            await Task.Delay(100);
        }
    }

    /// <summary>
    /// Alive means the pid still names a process carrying this creation stamp. A pid nobody holds —
    /// or one this process may no longer inspect, which is how a system process on an ordinary
    /// account reads — is not the survivor the kill under test left behind.
    /// </summary>
    public static bool IsStillAlive(ChildProcess child) =>
        TryRead(child.Pid) is { } current && current.StartedAt == child.StartedAt;

    /// <summary>
    /// Whether a process can be the child a command started: it has to have been created after the
    /// command was. That is what rules out a number that already belonged to something else before
    /// the child wrote it — the WmiPrvSE shape, which had been running for minutes.
    /// </summary>
    public static bool SpawnedByCommand(ChildProcess child, DateTime commandStarted) =>
        // A second of slack: a Windows creation stamp is quantised to the scheduler tick, so a
        // child started in the command's own instant can read a hair older than the command was.
        child.StartedAt >= commandStarted.AddSeconds(-1);

    /// <summary>The process behind a pid, or null when no process we may inspect holds it.</summary>
    public static ChildProcess? TryRead(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.HasExited
                ? null
                : new ChildProcess(pid, process.ProcessName, process.StartTime);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            // Exited between the lookup and the read, or a pid this process may not inspect:
            // neither is a survivor of the kill under test.
            return null;
        }
    }

    private static int? TryReadFile(string pidFile)
    {
        if (!File.Exists(pidFile))
        {
            return null;
        }

        try
        {
            string text = File.ReadAllText(pidFile).Trim();
            return int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int pid
            )
                ? pid
                : null;
        }
        catch (IOException)
        {
            // The writer may still hold the file briefly (Windows sharing); retry.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // The file exists but is momentarily unreadable; retry.
            return null;
        }
    }
}
