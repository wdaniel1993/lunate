namespace Lunate.TestSupport;

/// <summary>
/// Restores a copied fixture solution offline — the fixtures carry no packages and the source list
/// is cleared — and shares that work between tests instead of starting one child process per test.
/// </summary>
/// <remarks>
/// Every restore is a child <c>dotnet</c> process, and a suite that restores per test starts dozens of
/// MSBuild graphs at once on a loaded runner: that is where the gate flaked, with twelve empty
/// "restore failed" errors in one run. A restore is a function of its tree, so the artifacts of one
/// are reused by every test with the same tree (<see cref="RestoreCache"/>); the gate's two culture
/// passes and its parallel assemblies share them too. A failure that says something is never retried:
/// only a child that exits without a byte on either stream gets one retry, and both attempts are
/// reported when that fails too.
/// <para>
/// Every fixture the gate restores is an XML solution (<c>.slnx</c>), and a legacy <c>.sln</c> is
/// refused by name. The two formats do not restore the same way: MSBuild's legacy solution parser
/// writes a generated metaproject under the temp directory and builds that file, while <c>.slnx</c>
/// is expanded in memory. The signature CI kept producing for the one fixture that was still a
/// legacy <c>.sln</c> — <c>ValidateSolutionConfiguration</c> entered, not one per-project restore
/// line, <c>Build FAILED. 0 Warning(s) 0 Error(s)</c> — is what a generated metaproject without a
/// readable project list looks like, and its <c>.slnx</c> neighbour passed on the same runner. A
/// fixture that needs a real package graph still gets one from <c>.slnx</c>; nothing needs the legacy
/// parser to be restored, so the gate does not restore it.
/// </para>
/// <para>
/// The rest of the fix is isolation, because the failures outlived the first two attempts. Per-tree
/// packages (<c>NUGET_PACKAGES</c>, now also an explicit <c>--packages</c>) and one restore per test
/// process were already in place when CI 40676059477 failed: the twelve restore children in that log
/// start five to ten seconds apart, so they were not running together, and the retry still said
/// <c>Build FAILED. 0 Error(s)</c> immediately after <c>ValidateSolutionConfiguration</c> — the shape
/// of a build that was disturbed, not one that found a defect in the fixture. So every machine-wide
/// folder and process the child could share with another test is pinned to the fixture: the CLI home,
/// the package folder, the HTTP cache and the temp directory; the long-lived MSBuild server is turned
/// off; and the restore is limited to one node. On top of that, <see cref="RestoreCache.MachineLock"/>
/// bounds fixture restores to one at a time across every test process on the machine, which the
/// in-process semaphore beside it cannot see.
/// </para>
/// </remarks>
public static class FixtureRestore
{
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(120);

    private static readonly SemaphoreSlim InProcess = new(1, 1);

    private static int _restores;

    /// <summary>How many child restores this process has run — a cache hit costs none.</summary>
    public static int Restores => Volatile.Read(ref _restores);

    /// <summary>The offline configuration every fixture restore uses: no package sources at all.</summary>
    private const string Config = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
          </packageSources>
        </configuration>
        """;

    /// <summary>
    /// Restores <paramref name="solutionFileName"/> in <paramref name="root"/>, or copies the artifacts
    /// of an identical tree restored earlier.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="solutionFileName"/> is not a <c>.slnx</c> solution.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The restore failed; the message names the command, the directory, the exit code and the tail of
    /// what the child printed.
    /// </exception>
    public static void Restore(string root, string solutionFileName)
    {
        // A legacy .sln restore writes a generated metaproject into the temp directory and restores
        // that; .slnx is expanded in memory. Every gate fixture is .slnx (see the remarks), so a .sln
        // argument is a fixture that reintroduced the failure this helper exists to avoid — refused
        // here, at the call site, instead of on a loaded runner.
        if (
            !string.Equals(
                Path.GetExtension(solutionFileName),
                ".slnx",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new ArgumentException(
                $"the gate restores .slnx fixtures only; '{solutionFileName}' is not one. A legacy .sln "
                    + "restore writes a generated solution metaproject into the temp directory, the shape "
                    + "this suite failed in with 'Build FAILED. 0 Error(s)'; convert the fixture to .slnx.",
                nameof(solutionFileName)
            );
        }

        string configPath = Path.Combine(root, "NuGet.config");
        File.WriteAllText(configPath, Config);
        string solutionPath = Path.Combine(root, solutionFileName);
        string key = RestoreCache.Key(root, solutionFileName);

        InProcess.Wait();
        try
        {
            if (RestoreCache.TryReuse(key, root))
            {
                return;
            }

            // Bound the child restores of every test assembly on the machine, not just this one.
            using IDisposable? machineGate = RestoreCache.MachineLock();
            if (RestoreCache.TryReuse(key, root))
            {
                return;
            }

            using IDisposable? machineLock = RestoreCache.Lock(key);
            if (machineLock is not null && RestoreCache.TryReuse(key, root))
            {
                return;
            }

            Interlocked.Increment(ref _restores);
            Directory.CreateDirectory(ScratchDirectory(root));
            string[] arguments = RestoreArguments(root, solutionPath, configPath);
            ChildProcessResult result = DotnetCli.Run(
                root,
                arguments,
                ChildEnvironment(root),
                RestoreTimeout
            );

            // A restore that fails without a byte on either stream is not a broken fixture, it is a
            // child that died before it could speak. One retry separates "the fixture is wrong" from
            // "this process could not start": a real fixture defect fails twice with a reason, while a
            // transient start failure passes and leaves the suite honest. Both attempts are reported
            // when the retry fails too, so the next reader gets the second attempt's words as well.
            if (
                !result.Succeeded
                && string.IsNullOrEmpty(result.StandardOutput)
                && string.IsNullOrEmpty(result.StandardError)
            )
            {
                ChildProcessResult retry = DotnetCli.Run(
                    root,
                    [.. arguments, "--verbosity", "normal"],
                    ChildEnvironment(root),
                    RestoreTimeout
                );
                if (retry.Succeeded)
                {
                    RestoreCache.Store(key, root);
                    return;
                }

                throw new InvalidOperationException(
                    result.Describe("dotnet restore failed, silently, then failed again on a retry")
                        + Environment.NewLine
                        + retry.Describe("the retry")
                        + Environment.NewLine
                        + ChildProcessDiagnostics.Describe(root)
                );
            }

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Describe("dotnet restore failed"));
            }

            RestoreCache.Store(key, root);
        }
        finally
        {
            InProcess.Release();
        }
    }

    /// <summary>The exact argument list the restore child is given; a test asserts its bounds.</summary>
    internal static string[] RestoreArguments(
        string root,
        string solutionPath,
        string configPath
    ) =>
        [
            "restore",
            solutionPath,
            "--configfile",
            configPath,
            // Explicit as well as in the environment: a caller that scrubs the child's environment
            // still gets a per-fixture package folder instead of the machine's.
            "--packages",
            Path.Combine(root, ".packages"),
            "-p:NuGetAudit=false",
            // One node, no reuse: a restore of two projects does not need a fan-out, and a node is
            // another process a loaded runner can fail to schedule.
            "-maxcpucount:1",
            "-nodeReuse:false",
            "--nologo",
        ];

    /// <summary>The private temp directory (NuGet scratch, MSBuild response files) for one fixture.</summary>
    internal static string ScratchDirectory(string root) => Path.Combine(root, ".tmp");

    internal static Dictionary<string, string?> ChildEnvironment(string root) =>
        new(StringComparer.Ordinal)
        {
            // The child must not depend on the caller's home: a runner whose HOME is missing or
            // read-only makes dotnet exit 1 before it prints anything, which is indistinguishable
            // from a broken fixture.
            ["DOTNET_CLI_HOME"] = Path.Combine(root, ".dotnet-home"),
            ["NUGET_PACKAGES"] = Path.Combine(root, ".packages"),
            ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(root, ".nuget-http-cache"),
            // NuGet's scratch files and MSBuild's response files live under the temp directory.
            // Sharing it with every other restore on the machine is the last place they can collide.
            ["TMPDIR"] = ScratchDirectory(root),
            ["TMP"] = ScratchDirectory(root),
            ["TEMP"] = ScratchDirectory(root),
            // The build server outlives the command and is shared by every caller that finds its
            // pipe. A fixture restore has nothing to gain from it and something to lose.
            ["MSBUILDUSESERVER"] = "0",
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "false",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
        };
}
