using System.Diagnostics;
using System.Reflection;
using Lunate.Extensibility;
using Lunate.Extensibility.Restore;
using Lunate.Roslyn;

namespace Lunate.Extensibility.Compile.Tests;

/// <summary>
/// The behavioural test that settles MSBL001 (<c>Directory.Build.props</c> disables the locator's
/// build check in favour of this): a single-file extension is compiled — which loads
/// <c>NuGet.Frameworks</c> through the restore stack — and a real C# workspace is loaded through
/// <c>Lunate.Roslyn</c>'s own loader, which loads MSBuild and its copy of the same assembly. Both
/// orders run, each in its own process, because a loaded assembly cannot be unloaded and one order
/// would otherwise be decided by the other.
/// <para>
/// Hermetic: a temporary <c>HOME</c>, a temporary <c>NUGET_PACKAGES</c>, a local folder feed built in
/// the test, and a package-free workspace restored against a cleared source list. The assertions are
/// behavioural on both sides — a type compiled into the extension is invoked and a symbol in the
/// workspace is found — so a binding conflict that only breaks one of them cannot pass as "the load
/// returned a result".
/// </para>
/// </summary>
public sealed class MsblBindingOrderTests
{
    private const string OrderVariable = "LUNATE_MSBL_ORDER";
    private const string MarkerVariable = "LUNATE_MSBL_MARKER";
    private const string BuildFirst = "build-first";
    private const string WorkspaceFirst = "workspace-first";

    private const string Library = """
        namespace Acme.Tools;

        public static class Tools
        {
            public static string Hello() => "hello";
        }
        """;

    private const string Extension = """
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

        /// <summary>Dependency-free on purpose: the test loads the build and calls it.</summary>
        public static class Marker
        {
            public static string Value => "built";
        }
        """;

    private const string Project = """
        <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
            </PropertyGroup>
        </Project>
        """;

    private const string Class = """
        namespace Temp;

        public sealed class Class1
        {
            public static int Answer() => 42;
        }
        """;

    private const string Solution = """
        <Solution>
            <Project Path="project.csproj" />
        </Solution>
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_build_and_then_a_workspace_load_both_work() =>
        await RunOrderAsync(BuildFirst, nameof(A_build_and_then_a_workspace_load_both_work));

    [Fact]
    public async Task A_workspace_load_and_then_a_build_both_work() =>
        await RunOrderAsync(WorkspaceFirst, nameof(A_workspace_load_and_then_a_build_both_work));

    /// <summary>
    /// One order, in one process. The parent half only spawns the child: the order under test has to
    /// run where nothing has been loaded yet.
    /// </summary>
    private static async Task RunOrderAsync(string order, string method)
    {
        if (
            string.Equals(
                Environment.GetEnvironmentVariable(OrderVariable),
                order,
                StringComparison.Ordinal
            )
        )
        {
            await ProbeAsync(order);
            File.WriteAllText(Environment.GetEnvironmentVariable(MarkerVariable)!, order);
            return;
        }

        string marker = Path.Combine(
            Path.GetTempPath(),
            string.Concat("lunate-msbl-", order, "-", Guid.NewGuid().ToString("N"), ".marker")
        );
        try
        {
            using Process child = LaunchProbe(order, method, marker);
            Task<string> output = child.StandardOutput.ReadToEndAsync(Ct);
            Task<string> error = child.StandardError.ReadToEndAsync(Ct);
            bool exited = child.WaitForExit(TimeSpan.FromMinutes(10));
            string text = await output + await error;
            Assert.True(exited, $"the {order} probe timed out:\n{text}");
            Assert.True(
                child.ExitCode == 0,
                $"the {order} probe failed (exit {child.ExitCode}):\n{text}"
            );
            Assert.True(
                File.Exists(marker) && File.ReadAllText(marker) == order,
                $"the {order} probe never ran:\n{text}"
            );
        }
        finally
        {
            if (File.Exists(marker))
            {
                File.Delete(marker);
            }
        }
    }

    /// <summary>
    /// The order under test: both halves in one process, the named one second, and both asserted by
    /// using them — the extension's own type is invoked, and a symbol in the workspace is searched.
    /// </summary>
    private static async Task ProbeAsync(string order)
    {
        using CompileFixture fixture = CompileFixture.Create();
        using var temp = new TempDirectory();

        string home = Path.Combine(temp.Root, "home");
        string packages = Path.Combine(temp.Root, "nuget");
        string extensions = Path.Combine(home, ".lunate", "extensions");
        Directory.CreateDirectory(extensions);
        Directory.CreateDirectory(packages);
        string file = Path.Combine(extensions, "acme.cs");
        File.WriteAllText(file, Extension);

        var feed = new LocalPackageFeed(temp.Root);
        try
        {
            SeedFeed(fixture, feed, temp.Root);
            SeedTrust(home, file);

            var builder = new SingleFileExtensionBuilder(
                new ExtensionHostOptions { StorePath = Path.Combine(home, ".lunate") },
                () =>
                    new NuGetRestoreEngine(
                        new NuGetRestoreEngineOptions
                        {
                            Sources = [new SingleFilePackageSource("local", feed.FeedDirectory)],
                            GlobalPackagesFolder = packages,
                        }
                    ),
                () => new RoslynSingleFileCompiler(),
                globalPackagesFolder: packages
            );

            Func<Task<SingleFileBuildOutcome>> build = () =>
                builder.BuildAsync(file, ExtensionScope.Global, prompt: null, Ct);
            Func<Task<WorkspaceLoadResult>> workspace = () => LoadWorkspaceAsync(Ct);

            SingleFileBuildOutcome built;
            WorkspaceLoadResult loaded;
            if (order == BuildFirst)
            {
                built = await build();
                AssertBuildWorks(built);
                loaded = await workspace();
                AssertWorkspaceWorks(loaded);
            }
            else
            {
                loaded = await workspace();
                AssertWorkspaceWorks(loaded);
                built = await build();
                AssertBuildWorks(built);
            }

            // The first half is still usable when the second one is done: a conflict that only
            // breaks the earlier load would otherwise go unnoticed.
            AssertBuildWorks(built);
            AssertWorkspaceWorks(loaded);
        }
        finally
        {
            feed.Cleanup();
        }
    }

    /// <summary>
    /// The build half: the assembly on disk is the one the builder wrote, its type is loadable and
    /// its code runs. A <c>NuGet.Frameworks</c> conflict inside the restore stack fails here.
    /// </summary>
    private static void AssertBuildWorks(SingleFileBuildOutcome outcome)
    {
        Assert.True(
            outcome.Succeeded,
            "the build failed: " + (outcome.Reason ?? "no reason given")
        );
        string assembly = outcome.Build!.AssemblyPath;
        Assert.True(File.Exists(assembly), "the build wrote no assembly: " + assembly);

        Assembly loaded = Assembly.Load(File.ReadAllBytes(assembly));
        Type marker = loaded.GetType("Marker", throwOnError: false)!;
        Assert.NotNull(marker);
        Assert.Equal("built", marker.GetProperty("Value")!.GetValue(null));
    }

    /// <summary>
    /// The workspace half: <c>Lunate.Roslyn</c>'s loader reports a full load, and the compilation it
    /// holds answers a symbol search — a load result alone would not prove the workspace is usable.
    /// </summary>
    private static void AssertWorkspaceWorks(WorkspaceLoadResult load)
    {
        Assert.True(
            load.Status == WorkspaceStatus.Loaded,
            $"the workspace did not load: {load.Status} — {load.Message}"
        );
        Assert.Equal(1, load.ProjectCount);
        Assert.NotNull(load.SolutionPath);
    }

    /// <summary>
    /// A trivial workspace restored offline, then loaded through the product's own loader. The
    /// restore is a package-free project against a cleared source list, so nothing reaches a feed.
    /// </summary>
    private static async Task<WorkspaceLoadResult> LoadWorkspaceAsync(CancellationToken ct)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "lunate-msbl-workspace",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "project.csproj"), Project);
        File.WriteAllText(Path.Combine(root, "Class1.cs"), Class);
        File.WriteAllText(Path.Combine(root, "project.slnx"), Solution);

        try
        {
            Restore(root);
            using var backend = new RoslynBackend(root);
            WorkspaceLoadResult load = await backend.LoadAsync(ct);
            if (load.Status != WorkspaceStatus.Loaded)
            {
                return load;
            }

            SymbolSearchResult found = await backend.FindSymbolAsync("Class1", ct);
            Assert.True(
                found.Status is SymbolSearchStatus.Loaded or SymbolSearchStatus.Partial,
                $"the symbol search failed: {found.Status} — {found.Message}"
            );
            Assert.Contains(
                found.Matches,
                match => string.Equals(match.Name, "Class1", StringComparison.Ordinal)
            );
            return load;
        }
        finally
        {
            TryDelete(root);
        }
    }

    /// <summary>
    /// <c>dotnet restore</c> for the workspace, offline: a cleared source list, its own packages and
    /// CLI home, and the audit off (an audit would ask a feed for advisory data).
    /// </summary>
    private static void Restore(string root)
    {
        string config = Path.Combine(root, "NuGet.config");
        File.WriteAllText(
            config,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
              </packageSources>
            </configuration>
            """
        );

        var start = new ProcessStartInfo(DotNetPath())
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (
            string argument in new[]
            {
                "restore",
                Path.Combine(root, "project.slnx"),
                "--configfile",
                config,
                "-p:NuGetAudit=false",
                "--nologo",
            }
        )
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["NUGET_PACKAGES"] = Path.Combine(root, ".packages");
        start.Environment["DOTNET_CLI_HOME"] = Path.Combine(root, ".dotnet-home");
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";

        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("dotnet restore of the probe workspace timed out");
        }

        Assert.True(process.ExitCode == 0, "the probe workspace did not restore:\n" + output);
    }

    /// <summary>
    /// The locked package and the reference pack as real nupkgs on a local feed. The package's
    /// assembly is compiled by the compiler under test from the fixture's own reference pack, so the
    /// extension references an assembly that exists rather than a placeholder.
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

    /// <summary>The first-trust record an approving session writes; the probe never prompts.</summary>
    private static void SeedTrust(string home, string file)
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(File.ReadAllText(file));
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
    }

    /// <summary>
    /// The child that runs one order: the test assembly itself, filtered to this method, with its
    /// own home, packages folder and marker so the probe's world is not the suite's.
    /// </summary>
    private static Process LaunchProbe(string order, string method, string marker)
    {
        string assembly = typeof(MsblBindingOrderTests).Assembly.Location;
        string filter = string.Concat(typeof(MsblBindingOrderTests).FullName, ".", method);

        string? processPath = Environment.ProcessPath;
        bool direct =
            processPath is not null
            && string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "Lunate.Extensibility.Compile.Tests",
                StringComparison.Ordinal
            );

        var start = new ProcessStartInfo(direct ? processPath! : DotNetPath())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (!direct)
        {
            start.ArgumentList.Add("exec");
            start.ArgumentList.Add(assembly);
        }

        start.ArgumentList.Add("-method");
        start.ArgumentList.Add(filter);
        start.ArgumentList.Add("-noLogo");

        string home = Path.Combine(Path.GetDirectoryName(marker)!, "msbl-probe-home");
        // The home and its packages folder have to exist: `Environment.GetFolderPath(UserProfile)`
        // reads HOME and returns an empty string for a directory that is not there.
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(Path.Combine(home, "packages"));
        start.Environment[OrderVariable] = order;
        start.Environment[MarkerVariable] = marker;
        start.Environment["HOME"] = home;
        start.Environment["NUGET_PACKAGES"] = Path.Combine(home, "packages");
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        return Process.Start(start)!;
    }

    private static string DotNetPath()
    {
        string? host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return !string.IsNullOrEmpty(host) && File.Exists(host) ? host : "dotnet";
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
