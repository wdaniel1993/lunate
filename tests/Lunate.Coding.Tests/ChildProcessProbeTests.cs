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
}
