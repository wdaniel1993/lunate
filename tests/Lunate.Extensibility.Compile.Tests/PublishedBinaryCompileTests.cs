using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lunate.Extensibility;

namespace Lunate.Extensibility.Compile.Tests;

/// <summary>
/// Task 2.12: the artifact a user actually runs. A published self-contained single-file binary has
/// no SDK, no reference pack on disk and no assembly files beside it, so the compile has to bring its
/// own contract and restore its own reference pack — and the second run has to be a cache hit that
/// opens no feed. The test spawns the binary <c>scripts/verify.sh</c> publishes, in a temporary
/// <c>HOME</c> with a temporary <c>NUGET_PACKAGES</c>, against a local folder feed, with the feed made
/// unreadable for the second run: a request would fail the build, not go unnoticed.
/// </summary>
public sealed class PublishedBinaryCompileTests
{
    private const string Library = """
        namespace Acme.Tools;

        public static class Tools
        {
            public static string Hello() => "hello";
        }
        """;

    private const string Source = """
        #:extension acme-tools@1.0.0
        #:tool fetch_issue
        #:package Acme.Tools@2.0.0

        using Acme.Tools;
        using Lunate.Extensibility.Abstractions;

        public sealed class AcmeExtension : IExtensionFactory
        {
            public IExtension Create(IExtensionContext context) => new Shell(Tools.Hello());
        }

        internal sealed class Shell(string greeting) : IExtension
        {
            private readonly string _greeting = greeting;
        }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_published_single_file_build_compiles_with_an_empty_home_and_no_dotnet_on_path()
    {
        string? binary = PublishedBinary();
        if (binary is null)
        {
            Assert.Skip(
                "the published single-file binary is not on disk; run scripts/verify.sh (it publishes "
                    + "artifacts/publish/<rid>/lunate) — CI publishes on every leg"
            );
        }

        using CompileFixture fixture = CompileFixture.Create();
        using var temp = new TempDirectory();

        string home = Path.Combine(temp.Root, "home");
        string packages = Path.Combine(temp.Root, "nuget");
        string extensions = Path.Combine(home, ".lunate", "extensions");
        string tools = Path.Combine(temp.Root, "bin");
        Directory.CreateDirectory(extensions);
        Directory.CreateDirectory(packages);
        Directory.CreateDirectory(tools);
        string file = Path.Combine(extensions, "acme.cs");
        File.WriteAllText(file, Source);

        var feed = new LocalPackageFeed(temp.Root);
        try
        {
            SeedFeed(fixture, feed, temp.Root);
            feed.WriteConfig(extensions);

            // Trust is the user's decision and no CLI makes it: the record a session would have
            // written for this file is written here, before the binary runs.
            ExtensionHeader header = ExtensionHeaderScanner.Scan(Source);
            new ExtensionTrustStore(
                new ExtensionHostOptions { StorePath = Path.Combine(home, ".lunate") }
            ).RecordSingleFileTrust(
                file,
                SingleFileExtensionDigest.ComputeUnrestored(
                    header.Id!,
                    header.Packages,
                    File.ReadAllBytes(file)
                )
            );

            Run first = await LaunchAsync(binary!, tools, home, packages, temp.Root);
            Assert.True(
                first.Exit == 0,
                $"the published binary failed to build:\n{first.Output}\n{first.Error}"
            );
            Assert.Equal("built", State(first.Output));
            string assembly = Json(first.Output).GetProperty("assembly").GetString()!;
            Assert.True(File.Exists(assembly), $"the build's assembly is not on disk: {assembly}");
            Assert.StartsWith(
                Path.Combine(home, ".lunate", "cache", "extensions", "acme-tools", "builds"),
                assembly,
                StringComparison.Ordinal
            );

            // The reference pack came from the feed into the packages folder NUGET_PACKAGES names —
            // the published binary has no copy of it, so this is the restore 2.11 promises.
            Assert.True(
                Directory.Exists(
                    Path.Combine(packages, "microsoft.netcore.app.ref", fixture.RefPackVersion)
                ),
                "the reference pack was not restored into NUGET_PACKAGES"
            );
            Assert.True(
                Directory.Exists(Path.Combine(packages, "acme.tools", "2.0.0")),
                "the locked package was not restored into NUGET_PACKAGES"
            );

            // The second run: the feed is not where the config says it is, so a request would fail
            // the build rather than pass unseen. It is moved back afterwards and the snapshot has to
            // be unchanged, so nothing was read or rewritten either.
            string before = feed.Snapshot();
            string away = feed.FeedDirectory + ".away";
            Directory.Move(feed.FeedDirectory, away);
            Run second;
            try
            {
                Assert.False(Directory.Exists(feed.FeedDirectory));
                second = await LaunchAsync(binary!, tools, home, packages, temp.Root);
            }
            finally
            {
                Directory.Move(away, feed.FeedDirectory);
            }

            Assert.True(
                second.Exit == 0,
                $"the second run failed, so it read something it should not have:\n{second.Output}\n{second.Error}"
            );
            Assert.Equal("up-to-date", State(second.Output));
            Assert.Equal(before, feed.Snapshot());
        }
        finally
        {
            feed.Cleanup();
        }
    }

    /// <summary>
    /// The locked package and the reference pack as real nupkgs. The package's assembly is compiled
    /// by the compiler under test from the fixture's own reference pack, so the published binary
    /// references an assembly that exists rather than four bytes of filler.
    /// </summary>
    private static void SeedFeed(CompileFixture fixture, LocalPackageFeed feed, string root)
    {
        byte[] library = CompileFixture.Assembly(
            fixture.Compile(
                new SingleFileCompileRequest
                {
                    ExtensionId = "Acme.Tools",
                    ExtensionVersion = "2.0.0",
                    SourcePath = Path.Combine(root, "Acme.Tools.cs"),
                    SourceText = Library,
                    AssemblyName = "Acme.Tools",
                    Target = fixture.Target,
                    GlobalPackagesFolder = fixture.PackagesFolder,
                    Lock = fixture.Lock(),
                },
                Ct
            )
        );
        feed.Add(
            "Acme.Tools",
            "2.0.0",
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [FormattableString.Invariant($"lib/{fixture.Target.Tfm}/Acme.Tools.dll")] = library,
            }
        );
        feed.AddReferencePack(
            fixture.RefPackVersion,
            Path.Combine(
                fixture.PackagesFolder,
                "microsoft.netcore.app.ref",
                fixture.RefPackVersion.ToLowerInvariant(),
                "ref",
                fixture.Target.Tfm
            ),
            fixture.Target.Tfm
        );
    }

    private sealed record Run(int Exit, string Output, string Error);

    /// <summary>
    /// Runs <c>lunate extensions build acme-tools --json</c> with the child's whole world replaced:
    /// its own home, its own packages folder, and a PATH of one empty directory, so no <c>dotnet</c>
    /// can be found (proved by the assertion below rather than assumed).
    /// </summary>
    private static async Task<Run> LaunchAsync(
        string binary,
        string tools,
        string home,
        string packages,
        string working
    )
    {
        Assert.False(
            File.Exists(Path.Combine(tools, "dotnet")),
            "the child's PATH must not carry a dotnet"
        );

        var start = new ProcessStartInfo(binary)
        {
            WorkingDirectory = working,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[] { "extensions", "build", "acme-tools", "--json" })
        {
            start.ArgumentList.Add(argument);
        }

        // The state root is what discovery, trust and the cache read. HOME does not move it on
        // Windows — .NET resolves the profile through the shell — so the child is pointed at a
        // store directly; HOME and USERPROFILE move whatever else honours them.
        start.Environment["LUNATE_HOME"] = Path.Combine(home, ".lunate");
        start.Environment["HOME"] = home;
        start.Environment["USERPROFILE"] = home;
        start.Environment["NUGET_PACKAGES"] = packages;
        start.Environment["PATH"] = tools;
        start.Environment.Remove("DOTNET_ROOT");
        start.Environment.Remove("DOTNET_ROOT_ARM64");
        start.Environment.Remove("DOTNET_HOST_PATH");

        using var process = new Process { StartInfo = start };
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(5));
        await process.WaitForExitAsync(timeout.Token);
        return new Run(process.ExitCode, await output, await error);
    }

    private static string State(string json) =>
        Json(json).GetProperty("state").GetString() ?? string.Empty;

    /// <summary>The last non-empty line of a stream: the one JSON object the subcommand wrote.</summary>
    private static JsonElement Json(string text)
    {
        string line = text.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .Last();
        return JsonDocument.Parse(line).RootElement.Clone();
    }

    /// <summary>
    /// The binary <c>scripts/verify.sh</c> publishes, for the RID this machine runs: the same layout
    /// the startup budget and the install-script step read.
    /// </summary>
    private static string? PublishedBinary()
    {
        string rid = RuntimeInformation.RuntimeIdentifier;
        string name = rid.StartsWith("win", StringComparison.Ordinal) ? "lunate.exe" : "lunate";
        string path = Path.Combine(RepositoryRoot(), "artifacts", "publish", rid, name);
        return File.Exists(path) ? path : null;
    }

    private static string RepositoryRoot()
    {
        for (
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "lunate.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"could not find lunate.sln above {AppContext.BaseDirectory}"
        );
    }
}
