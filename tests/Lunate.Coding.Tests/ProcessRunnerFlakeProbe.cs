using System.Diagnostics;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// Diagnostic probe for the macOS CI flake in
/// <see cref="ProcessRunnerTests.The_line_handler_answers_every_stdout_line_in_order"/>.
/// Runs the exact same round trip with the same asserts, but the child script keeps a
/// synchronously-flushed log (start / ping-written / data:N / answer-written / stdin-end) so a
/// failure pinpoints which side lost the round trip. Lives only on the flake-probe branch (and in
/// the probe workflow) until the root cause is fixed.
/// </summary>
public sealed class ProcessRunnerFlakeProbe
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_probe_reproduces_the_line_handler_round_trip()
    {
        using var temp = new TempDirectory();
        string childLog = temp.File("child.log");
        var seen = new List<string>();
        // The log path travels as argv[1] (with -e, process.argv[1] is the first user argument) —
        // no path escaping into the script text.
        const string script =
            "const fs=require('fs');const p=process.argv[1];"
            + "const log=s=>{try{fs.appendFileSync(p,s+'\\n')}catch(e){}};"
            + "log('start '+(process.version||''));"
            + "process.stdout.write('ping\\n');"
            + "log('ping-written');"
            + "process.stdin.once('data',c=>{log('data:'+c.length);"
            + "process.stdout.write('got:'+c.toString().trim()+'\\n');"
            + "log('answer-written');process.exit(0);});"
            + "process.stdin.once('end',()=>log('stdin-end'));";

        var request = new ProcessRunRequest(
            "node",
            ["-e", script, childLog],
            temp.Root,
            Timeout: TimeSpan.FromSeconds(60)
        )
        {
            LineHandler = (line, _) =>
            {
                seen.Add(line);
                return Task.FromResult<string?>(line == "ping" ? "pong" : null);
            },
        };

        var runner = new ProcessRunner();
        Stopwatch elapsed = Stopwatch.StartNew();
        ProcessRunResult result = await runner.RunAsync(request, Ct);
        elapsed.Stop();

        bool ok =
            result.ExitCode == 0
            && seen.Count == 2
            && seen[0] == "ping"
            && seen[1] == "got:pong"
            && result.Stdout.Contains("got:pong", StringComparison.Ordinal);
        if (!ok)
        {
            string logText = System.IO.File.Exists(childLog)
                ? System.IO.File.ReadAllText(childLog).ReplaceLineEndings("\n")
                : "(no child.log — node never ran our script)";
            string? artifactDir = Environment.GetEnvironmentVariable("FLK_ARTIFACT_DIR");
            if (artifactDir is not null)
            {
                try
                {
                    Directory.CreateDirectory(artifactDir);
                    System.IO.File.WriteAllText(
                        Path.Combine(artifactDir, $"child-{Guid.NewGuid():N}.log"),
                        logText
                    );
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            Assert.Fail(
                "PROBE FAILURE\n"
                    + $"exit={result.ExitCode} elapsedMs={elapsed.ElapsedMilliseconds}\n"
                    + $"seen=[{string.Join(" | ", seen)}]\n"
                    + $"stdoutBytes={result.Stdout.Length} stdout=<<{result.Stdout.ReplaceLineEndings("\\n")}>>\n"
                    + $"stderr=<<{result.Stderr.ReplaceLineEndings("\\n")}>>\n"
                    + $"childLog=<<\n{logText}\n>>\n"
                    + $"PATH={Environment.GetEnvironmentVariable("PATH")}\n"
            );
        }
    }
}
