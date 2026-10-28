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
/// passes and its parallel assemblies share them too. Nothing is retried: a failing restore keeps the
/// child's output and fails the test.
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
    /// <exception cref="InvalidOperationException">
    /// The restore failed; the message names the command, the directory, the exit code and the tail of
    /// what the child printed.
    /// </exception>
    public static void Restore(string root, string solutionFileName)
    {
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

            using IDisposable? machineLock = RestoreCache.Lock(key);
            if (machineLock is not null && RestoreCache.TryReuse(key, root))
            {
                return;
            }

            Interlocked.Increment(ref _restores);
            string[] arguments =
            [
                "restore",
                solutionPath,
                "--configfile",
                configPath,
                "-p:NuGetAudit=false",
                "--nologo",
            ];
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

    internal static Dictionary<string, string?> ChildEnvironment(string root) =>
        new(StringComparer.Ordinal)
        {
            // The child must not depend on the caller's home: a runner whose HOME is missing or
            // read-only makes dotnet exit 1 before it prints anything, which is indistinguishable
            // from a broken fixture.
            ["DOTNET_CLI_HOME"] = Path.Combine(root, ".dotnet-home"),
            ["NUGET_PACKAGES"] = Path.Combine(root, ".packages"),
            ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(root, ".nuget-http-cache"),
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
        };
}
