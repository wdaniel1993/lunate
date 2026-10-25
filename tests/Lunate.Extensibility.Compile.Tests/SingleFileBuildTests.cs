using Lunate.Extensibility;

namespace Lunate.Extensibility.Compile.Tests;

/// <summary>
/// Tasks 2.7 and 2.8 at the service boundary: the builder owns the order (trust, then the lock that
/// answers for those bytes, then the cache, then the compiler) and the cache owns the layout and the
/// retention rule. The red cases are the ones the requirements name — a failed compile reports the
/// first diagnostic and leaves nothing behind, two builds survive a third, a toolset change is a new
/// key, and prune removes what no extension points at.
/// </summary>
public sealed class SingleFileBuildTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_failed_compile_reports_the_first_diagnostic_and_writes_no_build()
    {
        using CompileFixture fixture = CompileFixture.Create();
        ExtensionLockPackage package = fixture.AddPackage("Acme.Tools", "2.0.0", Lib, Ct);
        string path = fixture.WriteTrustedFile("acme.cs", Source("int first = Missing.One;"));
        var compiler = new CountingCompilerFactory();

        SingleFileBuildOutcome outcome = await BuildAsync(fixture, path, package, compiler);

        Assert.True(
            outcome.Status == SingleFileBuildStatus.CompileFailed,
            $"expected a compile failure, got {outcome.Status}: {outcome.Reason}"
        );
        Assert.Null(outcome.Build);
        Assert.Equal(SingleFileExtensionState.CompileFailed, outcome.Extension.State);
        Assert.True(
            outcome.Reason!.Contains("error CS0103", StringComparison.Ordinal),
            "reason was: " + outcome.Reason
        );
        Assert.True(
            outcome.Reason.StartsWith(path + "(", StringComparison.Ordinal),
            "reason was: " + outcome.Reason
        );
        Assert.IsType<SingleFileDiagnostic>(outcome.Diagnostic);

        // Nothing behind: no builds directory, no pointer, no assembly.
        Assert.False(
            Directory.Exists(SingleFileExtensionPaths.BuildsPath(fixture.Store, "acme-tools"))
        );
        Assert.False(
            File.Exists(SingleFileExtensionPaths.BuildsIndexPath(fixture.Store, "acme-tools"))
        );
        Assert.Equal(1, compiler.Creations);
    }

    [Fact]
    public async Task A_second_build_of_the_same_bytes_is_a_cache_hit()
    {
        using CompileFixture fixture = CompileFixture.Create();
        ExtensionLockPackage package = fixture.AddPackage("Acme.Tools", "2.0.0", Lib, Ct);
        string path = fixture.WriteTrustedFile("acme.cs", Source("int value = 1;"));
        var compiler = new CountingCompilerFactory();

        SingleFileBuildOutcome first = await BuildAsync(fixture, path, package, compiler);
        SingleFileBuildOutcome second = await BuildAsync(fixture, path, package, compiler);

        Assert.Equal(SingleFileBuildStatus.Built, first.Status);
        Assert.Equal(SingleFileBuildStatus.CacheHit, second.Status);
        Assert.Equal(first.Build!.Key, second.Build!.Key);
        Assert.Equal(first.Build.AssemblyPath, second.Build.AssemblyPath);
        // One compile in the process, and one build directory on disk.
        Assert.Equal(1, compiler.Creations);
        Assert.Equal(
            first.Build.Key,
            Assert.Single(
                Directory
                    .GetDirectories(
                        SingleFileExtensionPaths.BuildsPath(fixture.Store, "acme-tools")
                    )
                    .Select(Path.GetFileName)
            )
        );
    }

    [Fact]
    public async Task Two_builds_are_kept_and_the_third_evicts_the_oldest()
    {
        using CompileFixture fixture = CompileFixture.Create();
        ExtensionLockPackage package = fixture.AddPackage("Acme.Tools", "2.0.0", Lib, Ct);
        string path = fixture.WriteTrustedFile("acme.cs", Source("int value = 0;"));

        List<string> keys = [];
        foreach (int edit in new[] { 1, 2, 3 })
        {
            File.WriteAllText(path, Source(FormattableString.Invariant($"int value = {edit};")));
            SingleFileBuildOutcome outcome = await BuildAsync(
                fixture,
                path,
                package,
                new CountingCompilerFactory(),
                prompt: new ApprovingPrompt()
            );
            Assert.Equal(SingleFileBuildStatus.Built, outcome.Status);
            keys.Add(outcome.Build!.Key);
        }

        Assert.Equal(3, keys.Distinct(StringComparer.Ordinal).Count());
        string builds = SingleFileExtensionPaths.BuildsPath(fixture.Store, "acme-tools");
        Assert.Equal(
            new[] { keys[1], keys[2] }.Order(StringComparer.Ordinal),
            Directory.GetDirectories(builds).Select(Path.GetFileName).Order(StringComparer.Ordinal)
        );
        Assert.False(
            Directory.Exists(
                SingleFileExtensionPaths.BuildPath(fixture.Store, "acme-tools", keys[0])
            )
        );

        // The pointer names the newest build and the entry keeps exactly two.
        var cache = new SingleFileBuildCache(fixture.Store);
        Assert.Equal(keys[2], cache.Current("acme-tools")!.Key);
        Assert.Equal(new[] { keys[2], keys[1] }, cache.Keys("acme-tools").Take(2));
    }

    [Fact]
    public async Task A_toolset_change_produces_a_new_key_and_keeps_both_builds()
    {
        using CompileFixture fixture = CompileFixture.Create();
        ExtensionLockPackage package = fixture.AddPackage("Acme.Tools", "2.0.0", Lib, Ct);
        string path = fixture.WriteTrustedFile("acme.cs", Source("int value = 1;"));
        var prompt = new ApprovingPrompt();

        SingleFileBuildOutcome arm = await BuildAsync(
            fixture,
            path,
            package,
            new CountingCompilerFactory(),
            prompt: prompt
        );
        SingleFileBuildOutcome other = await BuildAsync(
            fixture,
            path,
            package,
            new CountingCompilerFactory(),
            prompt: prompt,
            target: fixture.Target with
            {
                Rid = "linux-x64",
            }
        );

        Assert.Equal(SingleFileBuildStatus.Built, arm.Status);
        Assert.Equal(SingleFileBuildStatus.Built, other.Status);
        Assert.NotEqual(arm.Build!.Key, other.Build!.Key);
        // The toolset half is not a trust question: neither build asked, and the same decision
        // covers both.
        Assert.Equal(0, prompt.Calls);
        Assert.Equal(
            new[] { arm.Build.Key, other.Build.Key }.Order(StringComparer.Ordinal),
            Directory
                .GetDirectories(SingleFileExtensionPaths.BuildsPath(fixture.Store, "acme-tools"))
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
        );
    }

    [Fact]
    public async Task A_stale_file_is_never_compiled_and_reaches_no_compiler()
    {
        using CompileFixture fixture = CompileFixture.Create();
        ExtensionLockPackage package = fixture.AddPackage("Acme.Tools", "2.0.0", Lib, Ct);
        string path = fixture.WriteTrustedFile("acme.cs", Source("int value = 1;"));
        await BuildAsync(fixture, path, package, new CountingCompilerFactory());

        File.AppendAllText(path, "\n// one edit\n");
        var compiler = new CountingCompilerFactory();
        SingleFileBuildOutcome outcome = await BuildAsync(fixture, path, package, compiler);

        Assert.Equal(SingleFileBuildStatus.TrustRequired, outcome.Status);
        Assert.Equal(0, compiler.Creations);
    }

    [Fact]
    public async Task Prune_removes_an_entry_no_extension_points_at()
    {
        using CompileFixture fixture = CompileFixture.Create();
        ExtensionLockPackage package = fixture.AddPackage("Acme.Tools", "2.0.0", Lib, Ct);
        string live = fixture.WriteTrustedFile("acme.cs", Source("int value = 1;"));
        SingleFileBuildOutcome kept = await BuildAsync(
            fixture,
            live,
            package,
            new CountingCompilerFactory()
        );
        Assert.Equal(SingleFileBuildStatus.Built, kept.Status);

        // A second file that builds and is then deleted: its entry is the orphan.
        string gone = Path.Combine(fixture.Extensions, "gone.cs");
        File.WriteAllText(
            gone,
            Source("int value = 1;").Replace("acme-tools", "gone-tools", StringComparison.Ordinal)
        );
        new ExtensionTrustStore(fixture.Options()).RecordSingleFileTrust(
            gone,
            SingleFileExtensionDigest.ComputeUnrestored(
                "gone-tools",
                ExtensionHeaderScanner.Scan(File.ReadAllText(gone)).Packages,
                File.ReadAllBytes(gone)
            )
        );
        SingleFileBuildOutcome built = await BuildAsync(
            fixture,
            gone,
            package,
            new CountingCompilerFactory()
        );
        Assert.Equal(SingleFileBuildStatus.Built, built.Status);
        Assert.True(
            Directory.Exists(SingleFileExtensionPaths.EntryPath(fixture.Store, "gone-tools"))
        );

        File.Delete(gone);
        var loader = new ExtensionLoader(fixture.Options());
        loader.Discover(fixture.Store, fixture.Store);
        IReadOnlyList<string> removed = SingleFileCachePruner.Prune(
            fixture.Store,
            [.. loader.SingleFileExtensions.Select(candidate => candidate.Id!).OfType<string>()]
        );

        Assert.Equal(["gone-tools"], removed);
        Assert.False(
            Directory.Exists(SingleFileExtensionPaths.EntryPath(fixture.Store, "gone-tools"))
        );
        // The live entry keeps its lock and its build.
        Assert.True(File.Exists(SingleFileExtensionPaths.LockPath(fixture.Store, "acme-tools")));
        Assert.NotNull(new SingleFileBuildCache(fixture.Store).Current("acme-tools"));
        Assert.True(File.Exists(live));
    }

    [Fact]
    public void A_build_key_is_the_documented_formula_truncated_to_hex()
    {
        string key = SingleFileBuildKey.Compute("source", "lock", "1.11.0", "net10.0", "osx-arm64");

        Assert.Equal(SingleFileBuildKey.Length, key.Length);
        Assert.True(SingleFileBuildKey.IsKey(key));
        Assert.Equal(
            key,
            SingleFileBuildKey.Compute("source", "lock", "1.11.0", "net10.0", "osx-arm64")
        );
        // Every input moves it: a source edit, a new lock (either half), a host update, a toolset.
        Assert.NotEqual(
            key,
            SingleFileBuildKey.Compute("source2", "lock", "1.11.0", "net10.0", "osx-arm64")
        );
        Assert.NotEqual(
            key,
            SingleFileBuildKey.Compute("source", "lock2", "1.11.0", "net10.0", "osx-arm64")
        );
        Assert.NotEqual(
            key,
            SingleFileBuildKey.Compute("source", "lock", "1.12.0", "net10.0", "osx-arm64")
        );
        Assert.NotEqual(
            key,
            SingleFileBuildKey.Compute("source", "lock", "1.11.0", "net9.0", "osx-arm64")
        );
        Assert.NotEqual(
            key,
            SingleFileBuildKey.Compute("source", "lock", "1.11.0", "net10.0", "linux-x64")
        );
    }

    /// <summary>The library one locked package ships, compiled by the compiler under test.</summary>
    private const string Lib = """
        namespace Acme.Tools;

        public static class Tools
        {
            public static string Hello() => "hello";
        }
        """;

    /// <summary>
    /// A buildable extension: the header the digest covers, the contract's factory, and a body a
    /// test swaps to produce a new source hash (or a diagnostic).
    /// </summary>
    private static string Source(string body) =>
        $$"""
            #:extension acme-tools@1.0.0
            #:tool fetch_issue
            #:package Acme.Tools@2.0.0

            using Acme.Tools;
            using Lunate.Extensibility.Abstractions;

            public sealed class AcmeExtension : IExtensionFactory
            {
                public IExtension Create(IExtensionContext context) => new Shell(Tools.Hello());

                private static void Edited()
                {
                    {{body}}
                }
            }

            internal sealed class Shell(string greeting) : IExtension
            {
                private readonly string _greeting = greeting;
            }
            """;

    private static Task<SingleFileBuildOutcome> BuildAsync(
        CompileFixture fixture,
        string path,
        ExtensionLockPackage package,
        CountingCompilerFactory compiler,
        IExtensionTrustPrompt? prompt = null,
        SingleFileTarget? target = null
    )
    {
        SingleFileTarget resolved = target ?? fixture.Target;
        var engine = new FakeRestoreEngine(fixture, [package]);
        var builder = new SingleFileExtensionBuilder(
            fixture.Options(),
            () => engine,
            compiler.Create,
            resolved,
            fixture.PackagesFolder
        );
        return builder.BuildAsync(path, ExtensionScope.Global, prompt, Ct);
    }
}
