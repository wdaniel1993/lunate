using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Xunit.Sdk;

namespace Lunate.Coding.Tests;

/// <summary>
/// The rule the tree-kill assertions rest on: they watch a process, not a number.
/// </summary>
public sealed class ChildProcessProbeTests
{
    [Fact]
    public void A_survivor_is_judged_by_start_time_not_by_the_pid_alone()
    {
        // Three outcomes the rule has to give: the process we pinned and that is still running reads
        // as alive; the same number worn by a process created a second later — exactly what the
        // runner hands back after recycling — reads as gone; a pid nobody holds reads as gone. A
        // comparison that never matched would make every kill assertion pass without ever looking
        // at a survivor.
        if (ChildProcessProbe.TryRead(Environment.ProcessId) is not { } self)
        {
            Assert.Fail("the test process must be readable from its own pid");
            return;
        }

        Assert.True(ChildProcessProbe.IsStillAlive(self), "a live process read as alive");
        Assert.False(
            ChildProcessProbe.IsStillAlive(self with { StartedAt = self.StartedAt.AddSeconds(1) }),
            "another process wearing the pid read as the survivor"
        );
        Assert.False(
            ChildProcessProbe.IsStillAlive(
                new ChildProcess(int.MaxValue, self.Name, self.StartedAt)
            ),
            "a pid nobody holds read as the survivor"
        );
    }

    [Fact]
    public async Task A_number_a_stranger_holds_is_not_a_survivor_of_the_kill()
    {
        // Run 40704788358 in miniature: the pid file named a number, and by the time the test asked,
        // that number belonged to a process the test had never started. The identity here carries a
        // stamp a minute older than the process actually wearing the pid, so the wait sees "not
        // that process" at once. Watching the bare number instead spends the whole survivor budget
        // on the test's own process and then reports it as a survivor — which is what a WmiPrvSE
        // was reported as being.
        if (ChildProcessProbe.TryRead(Environment.ProcessId) is not { } self)
        {
            Assert.Fail("the test process must be readable from its own pid");
            return;
        }

        await ChildProcessProbe.AssertEventuallyDeadAsync(
            self with
            {
                StartedAt = self.StartedAt.AddMinutes(-1),
            }
        );
    }

    [Fact]
    public void A_process_older_than_the_command_is_not_its_child()
    {
        // The other half of "which relation put this pid in the tree": a file naming a process that
        // already existed when the command started was not written by that command's child, so the
        // test refuses it by name instead of watching it.
        if (ChildProcessProbe.TryRead(Environment.ProcessId) is not { } self)
        {
            Assert.Fail("the test process must be readable from its own pid");
            return;
        }

        Assert.True(
            ChildProcessProbe.SpawnedByCommand(self, self.StartedAt),
            "the process's own creation stamp is the boundary"
        );
        Assert.True(
            ChildProcessProbe.SpawnedByCommand(self, self.StartedAt.AddMilliseconds(500)),
            "a command started in the same instant as the child can read a hair later"
        );
        Assert.False(
            ChildProcessProbe.SpawnedByCommand(self, self.StartedAt.AddSeconds(2)),
            "a process already running when the command started is not its child"
        );
    }

    [Fact]
    public async Task A_missing_file_reports_the_command_and_where_it_searched()
    {
        // #286: the Windows leg reported only "no pid was written within 60 seconds", which fits a
        // shell that never started, a relative write that landed in another working directory, and
        // a starved runner alike. The expiry has to carry what is known instead.
        using var temp = new TempDirectory();
        string pidFile = temp.File("grandchild.pid");
        var stillRunning = new TaskCompletionSource().Task;

        FailException failure = await Assert.ThrowsAsync<FailException>(() =>
            ChildProcessProbe.AwaitWrittenAsync(
                pidFile,
                DateTime.Now,
                CancellationToken.None,
                new CommandUnderTest(temp.Root) { Run = stillRunning },
                WaitBudgets.ProbeExpiry
            )
        );

        Assert.Contains($"no pid was pinned from {pidFile}", failure.Message);
        Assert.Contains("the file never appeared at the path the probe watched", failure.Message);
        Assert.Contains("the command was still running", failure.Message);
        Assert.Contains(
            $"no file named grandchild.pid exists anywhere under {temp.Root}",
            failure.Message
        );
    }

    [Fact]
    public async Task A_file_written_in_another_directory_is_named_in_the_failure()
    {
        // The second explanation, and the one a larger window would hide: the child wrote its
        // relative path against a working directory the test was not watching. The answer names
        // the path it did land on.
        using var temp = new TempDirectory();
        string watched = temp.File("grandchild.pid");
        string elsewhere = Path.Combine(temp.Root, "deeper", "grandchild.pid");
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);
        File.WriteAllText(elsewhere, "4242");

        FailException failure = await Assert.ThrowsAsync<FailException>(() =>
            ChildProcessProbe.AwaitWrittenAsync(
                watched,
                DateTime.Now,
                CancellationToken.None,
                new CommandUnderTest(temp.Root),
                WaitBudgets.ProbeExpiry
            )
        );

        Assert.Contains(elsewhere, failure.Message);
        Assert.Contains("wrote it in a different working directory", failure.Message);
    }

    [Fact]
    public async Task A_file_that_never_held_a_pid_is_reported_with_its_text()
    {
        // The window can also expire with a file present: a partial write, or a child that created
        // the file and died before filling it. The text is the evidence, so the message carries it.
        using var temp = new TempDirectory();
        string pidFile = temp.File("grandchild.pid");
        File.WriteAllText(pidFile, "not-a-pid");

        FailException failure = await Assert.ThrowsAsync<FailException>(() =>
            ChildProcessProbe.AwaitWrittenAsync(
                pidFile,
                DateTime.Now,
                CancellationToken.None,
                new CommandUnderTest(temp.Root),
                WaitBudgets.ProbeExpiry
            )
        );

        Assert.Contains("the probe read \"not-a-pid\" from the file", failure.Message);
        Assert.Contains("which is not a pid", failure.Message);
    }

    [Fact]
    public async Task A_command_that_never_ran_is_reported_without_waiting_out_the_window()
    {
        // The first explanation: Process.Start threw, so there is no child and no later write to
        // wait for. The probe observes the run task and stops on the first poll, naming the
        // exception instead of spending the spawn window on it.
        using var temp = new TempDirectory();
        string pidFile = temp.File("grandchild.pid");
        Task failed = Task.FromException(
            new Win32Exception(2, "The system cannot find the file specified.")
        );
        var elapsed = Stopwatch.StartNew();

        FailException failure = await Assert.ThrowsAsync<FailException>(() =>
            ChildProcessProbe.AwaitWrittenAsync(
                pidFile,
                DateTime.Now,
                CancellationToken.None,
                new CommandUnderTest(temp.Root) { Run = failed }
            )
        );
        elapsed.Stop();

        Assert.Contains("the command failed before its child wrote a pid", failure.Message);
        Assert.Contains("Win32Exception", failure.Message);
        Assert.Contains("The system cannot find the file specified.", failure.Message);
        Assert.True(
            elapsed.Elapsed < WaitBudgets.RefusalGrace,
            string.Create(
                CultureInfo.InvariantCulture,
                $"the probe waited {elapsed.Elapsed.TotalSeconds:0.###}s on a command that had already failed instead of reporting it on the first poll"
            )
        );
    }

    [Fact]
    public async Task A_command_that_ended_before_the_pin_says_how_it_ended()
    {
        // When the run wins the race the test closes the probe window; the message has to say how
        // the run ended, because "timed out after 60 seconds" and "exit code 0" need different
        // answers from the reader.
        using var temp = new TempDirectory();
        string pidFile = temp.File("grandchild.pid");
        Task<int> finished = Task.FromResult(7);
        using var window = new CancellationTokenSource();
        window.Cancel();

        FailException failure = await Assert.ThrowsAsync<FailException>(() =>
            ChildProcessProbe.AwaitWrittenAsync(
                pidFile,
                DateTime.Now,
                window.Token,
                new CommandUnderTest(temp.Root)
                {
                    Run = finished,
                    DescribeOutcome = () =>
                        string.Create(CultureInfo.InvariantCulture, $"exit code {finished.Result}"),
                }
            )
        );

        Assert.Contains("the command ended before the grandchild reported a pid", failure.Message);
        Assert.Contains("the command finished: exit code 7", failure.Message);
    }
}
