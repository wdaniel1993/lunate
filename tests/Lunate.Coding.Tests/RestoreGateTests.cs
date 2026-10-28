using System.Globalization;
using Lunate.TestSupport;

namespace Lunate.Coding.Tests;

/// <summary>
/// The fixture restores of every test process on the machine share one gate: at most one child
/// restore runs at a time. The in-process semaphore in <see cref="FixtureRestore"/> cannot see the
/// other assemblies the gate runs beside, and those are what the Ubuntu leg's silent restores
/// collided with.
/// </summary>
public sealed class RestoreGateTests
{
    private const string Method = nameof(Restores_narrow_to_one_process_at_a_time);
    private const string LogVariable = "LUNATE_TEST_RESTORE_GATE_LOG";

    [Fact]
    public async Task Restores_narrow_to_one_process_at_a_time()
    {
        if (TestChildProcess.IsChild(Method))
        {
            Probe();
            return;
        }

        using var temp = new TempDirectory();
        string log = temp.File("intervals.log");
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [LogVariable] = log,
        };

        // Both children are started before either is awaited, so they really do compete for the
        // gate rather than running back to back by accident.
        (int ExitCode, string Output)[] results = await Task.WhenAll(
            TestChildProcess.RunAsync(typeof(RestoreGateTests), Method, environment),
            TestChildProcess.RunAsync(typeof(RestoreGateTests), Method, environment)
        );

        Assert.All(
            results,
            result =>
                Assert.True(
                    result.ExitCode == 0,
                    $"a probe failed (exit {result.ExitCode}):{Environment.NewLine}{result.Output}"
                )
        );

        var intervals = File.ReadAllLines(log)
            .Select(line => line.Split(' ', 2))
            .GroupBy(parts => parts[0], StringComparer.Ordinal)
            .Select(group =>
            {
                long[] stamps = group
                    .Select(parts => long.Parse(parts[1], CultureInfo.InvariantCulture))
                    .ToArray();
                return (Start: stamps[0], End: stamps[^1]);
            })
            .OrderBy(interval => interval.Start)
            .ToArray();

        Assert.Equal(2, intervals.Length);
        Assert.True(
            intervals[0].End <= intervals[1].Start,
            "two processes held the restore gate at the same time"
        );
    }

    private static void Probe()
    {
        string log =
            Environment.GetEnvironmentVariable(LogVariable)
            ?? throw new InvalidOperationException(
                $"{LogVariable} is not set for the child probe."
            );
        using IDisposable? gate = RestoreCache.MachineLock();
        Append(log);
        // Long enough that two ungated probes overlap by a wide margin, short enough to keep the
        // test a second.
        Thread.Sleep(500);
        Append(log);
    }

    private static void Append(string path)
    {
        // FileShare.ReadWrite so the two (ungated) probes can still write the evidence that they
        // overlapped; the gate serializes them in the fixed behaviour.
        using var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite
        );
        using var writer = new StreamWriter(stream);
        writer.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Environment.ProcessId} {DateTime.UtcNow.Ticks}"
            )
        );
    }
}
