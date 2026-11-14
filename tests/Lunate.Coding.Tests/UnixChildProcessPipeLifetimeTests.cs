using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The descriptor contract of a POSIX spawn (#699): a child inherits only the descriptors its own
/// spawn names, so one spawn can never hold another spawn's pipe write end open. The overlapping
/// spawns below are the regression: on the pre-fix spawn every victim's captured stream stayed open
/// while a bystander lived, and a child's descriptor list carried pipe ends it was never given.
/// <para>
/// The descriptor-list test reads a child's table against a baseline of this process's, so it holds
/// only while nothing else in the process opens a descriptor: one that another test opens without
/// close-on-exec is copied into the child. Its collection therefore runs alone
/// (<see cref="SpawnDescriptorTableCollection"/>).
/// </para>
/// </summary>
[Collection(SpawnDescriptorTableCollection.Name)]
public sealed class UnixChildProcessPipeLifetimeTests
{
    [Fact]
    public void The_collection_never_runs_beside_another_collection()
    {
        // Sequential tests inside the collection are not enough: a process that another collection
        // starts mid-spawn holds descriptors in the parent that the probes below would inherit and
        // report. This pins the definition that keeps those foreign ends out of the probes (#862).
        CollectionDefinitionAttribute definition = Assert.IsType<CollectionDefinitionAttribute>(
            typeof(SpawnDescriptorTableCollection).GetCustomAttribute<CollectionDefinitionAttribute>()
        );
        Assert.True(
            definition.DisableParallelization,
            "the spawn-descriptor-table collection must disable parallelization"
        );
    }

    // Enough simultaneous spawns that the pipe-creation-to-posix_spawn window always overlaps; the
    // pre-fix spawn leaked in every pair of the measured run (#699).
    private const int Pairs = 8;

    [Fact]
    public async Task A_finished_child_stdout_reaches_eof_while_a_concurrent_child_lives()
    {
        SkipOnWindowsBecauseThisIsThePosixSpawnContract();
        using var temp = new TempDirectory();

        IReadOnlyList<string> openStreams = await RunPairsAsync(
            temp,
            victimScript: "echo victim",
            readStderr: false
        );

        Assert.True(
            openStreams.Count == 0,
            "victim stdout never reached EOF while a concurrent bystander was alive: "
                + string.Join(", ", openStreams)
        );
    }

    [Fact]
    public async Task A_finished_child_stderr_reaches_eof_while_a_concurrent_child_lives()
    {
        SkipOnWindowsBecauseThisIsThePosixSpawnContract();
        using var temp = new TempDirectory();

        IReadOnlyList<string> openStreams = await RunPairsAsync(
            temp,
            victimScript: "echo victim >&2",
            readStderr: true
        );

        Assert.True(
            openStreams.Count == 0,
            "victim stderr never reached EOF while a concurrent bystander was alive: "
                + string.Join(", ", openStreams)
        );
    }

    [Fact]
    public async Task A_child_holds_no_pipe_end_it_was_not_given()
    {
        SkipOnWindowsBecauseThisIsThePosixSpawnContract();
        using var temp = new TempDirectory();

        string baseline = await FdSetAsync(temp, "baseline.txt");

        // The probe children run beside long-lived bystanders, so any pipe end another spawn of this
        // test created is present in the parent's descriptor table at the instant they start. No
        // other test runs beside this one, so no descriptor enters that table from outside.
        using var barrier = new Barrier(Pairs * 2);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probes = new Task[Pairs];
        var bystanders = new Task[Pairs];
        for (var pair = 0; pair < Pairs; pair++)
        {
            int index = pair;
            probes[index] = Task.Run(
                async () =>
                {
                    barrier.SignalAndWait(TestContext.Current.CancellationToken);
                    await FdSetAsync(temp, FileName(index));
                },
                TestContext.Current.CancellationToken
            );
            bystanders[index] = Task.Run(
                async () =>
                {
                    barrier.SignalAndWait(TestContext.Current.CancellationToken);
                    using var child = UnixChildProcess.Spawn(
                        new ProcessRunRequest("/bin/sleep", ["30"], temp.Root)
                    );
                    await release.Task.WaitAsync(TestContext.Current.CancellationToken);
                    await child.KillTreeAsync();
                },
                TestContext.Current.CancellationToken
            );
        }

        await Task.WhenAll(probes);
        release.SetResult();
        await Task.WhenAll(bystanders);

        var unexpectedFds = new ConcurrentBag<string>();
        for (var pair = 0; pair < Pairs; pair++)
        {
            string listed = await File.ReadAllTextAsync(
                Path.Combine(temp.Root, FileName(pair)),
                TestContext.Current.CancellationToken
            );
            foreach (string fd in Fds(listed).Except(Fds(baseline)))
            {
                unexpectedFds.Add($"{FileName(pair)} holds fd {fd}");
            }
        }

        string[] unexpected = unexpectedFds.Distinct().Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            unexpected.Length == 0,
            "a child inherited pipe ends it was not given: "
                + string.Join(", ", unexpected.Take(12))
                + (
                    unexpected.Length > 12
                        ? string.Create(
                            CultureInfo.InvariantCulture,
                            $" (and {unexpected.Length - 12} more)"
                        )
                        : string.Empty
                )
        );
    }

    /// <summary>
    /// Starts <see cref="Pairs"/> victims and <see cref="Pairs"/> long-lived bystanders on one
    /// barrier, reads each victim's chosen stream with a deadline shorter than the bystander's
    /// lifetime, and returns the pairs whose stream never reached EOF. The bystanders are held until
    /// every victim has been observed, so a victim can never pass because its writer died.
    /// </summary>
    private static async Task<IReadOnlyList<string>> RunPairsAsync(
        TempDirectory temp,
        string victimScript,
        bool readStderr
    )
    {
        using var barrier = new Barrier(Pairs * 2);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var victims = new Task<string?>[Pairs];
        var bystanders = new Task[Pairs];
        for (var pair = 0; pair < Pairs; pair++)
        {
            int index = pair;
            victims[index] = Task.Run(
                async () =>
                {
                    barrier.SignalAndWait(TestContext.Current.CancellationToken);
                    using var child = UnixChildProcess.Spawn(
                        new ProcessRunRequest("/bin/sh", ["-c", victimScript], temp.Root)
                    );
                    Task<string> read = readStderr
                        ? child.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken)
                        : child.StandardOutput.ReadToEndAsync(
                            TestContext.Current.CancellationToken
                        );
                    try
                    {
                        await read.WaitAsync(
                            WaitBudgets.PipeEof,
                            TestContext.Current.CancellationToken
                        );
                        return null;
                    }
                    catch (TimeoutException)
                    {
                        return string.Create(CultureInfo.InvariantCulture, $"pair {index}");
                    }
                    finally
                    {
                        await child.WaitForExitAsync(TestContext.Current.CancellationToken);
                    }
                },
                TestContext.Current.CancellationToken
            );
            bystanders[index] = Task.Run(
                async () =>
                {
                    barrier.SignalAndWait(TestContext.Current.CancellationToken);
                    using var child = UnixChildProcess.Spawn(
                        new ProcessRunRequest("/bin/sleep", ["30"], temp.Root)
                    );
                    await release.Task.WaitAsync(TestContext.Current.CancellationToken);
                    await child.KillTreeAsync();
                },
                TestContext.Current.CancellationToken
            );
        }

        string?[] observed = await Task.WhenAll(victims);
        release.SetResult();
        await Task.WhenAll(bystanders);

        return observed.Where(name => name is not null).Select(name => name!).ToArray();
    }

    private static string FileName(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"fds-{index}.txt");

    /// <summary>Spawns one probe that lists its own descriptors into a file under the temp root.</summary>
    private static async Task<string> FdSetAsync(TempDirectory temp, string fileName)
    {
        string directory = OperatingSystem.IsMacOS() ? "/dev/fd" : "/proc/self/fd";
        var request = new ProcessRunRequest(
            "/bin/sh",
            [
                "-c",
                $"for fd in {directory}/*; do printf '%s\\n' \"${{fd##*/}}\"; done > '{fileName}'",
            ],
            temp.Root
        );
        using var child = UnixChildProcess.Spawn(request);
        // The probe's own stdout is not read: under the defect a bystander holds its write end, and
        // the file (not the pipe) is what this test observes.
        await child.WaitForExitAsync(TestContext.Current.CancellationToken);
        return await File.ReadAllTextAsync(
            Path.Combine(temp.Root, fileName),
            TestContext.Current.CancellationToken
        );
    }

    private static IEnumerable<string> Fds(string listed) =>
        listed.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim());

    private static void SkipOnWindowsBecauseThisIsThePosixSpawnContract()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip(
                "the descriptor-inheritance contract is POSIX-only; Windows redirected handles are pinned elsewhere"
            );
        }
    }
}
