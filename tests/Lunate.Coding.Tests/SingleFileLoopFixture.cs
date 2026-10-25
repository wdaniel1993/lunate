using System.Globalization;
using System.Runtime.InteropServices;
using Lunate.Extensibility;
using Lunate.Extensibility.Compile;

namespace Lunate.Coding.Tests;

/// <summary>
/// One single-file extension's world for a session-level test: a temp store and worktree, a
/// packages folder holding the running runtime's reference assemblies and one compiled package,
/// and the restore and compile seams a composed session needs. The package and the extension are
/// compiled by the real compiler against the machine's reference assemblies, so a run in which
/// "the model wrote a file declaring one package" exercises the whole path instead of simulating
/// it. A machine with neither a NuGet cache nor an SDK targeting pack skips the family with the
/// reason, the way the compile suite does, because a compile against a fabricated pack would
/// prove nothing.
/// </summary>
internal sealed class SingleFileLoopFixture : IDisposable
{
    /// <summary>The id the extension declares; it is also the assembly's simple name.</summary>
    internal const string Id = "acme-tools";

    /// <summary>The one package the file declares, and the code it compiles against.</summary>
    internal const string PackageId = "Acme.Tools";

    internal const string PackageVersion = "2.0.0";

    /// <summary>The package's own code: what proves the compile read the declared package.</summary>
    internal const string PackageSource = """
        namespace Acme.Tools;

        public static class Tools
        {
            public static string Greeting() => "hello from the package";
        }
        """;

    private const string ReferencePackId = "Microsoft.NETCore.App.Ref";

    private readonly TempDirectory _temp;
    private readonly ExtensionLockPackage _package;

    private SingleFileLoopFixture(
        TempDirectory temp,
        string packagesFolder,
        string packVersion,
        ExtensionLockReferencePack referencePack
    )
    {
        _temp = temp;
        Packages = packagesFolder;
        ReferencePack = referencePack;
        Target = SingleFileTarget.Current();
        Store = temp.File("store");
        Worktree = temp.File("worktree");
        ExtensionsRoot = Path.Combine(Worktree, ".lunate", "extensions");
        Directory.CreateDirectory(ExtensionsRoot);
        _package = AddPackage(packVersion);
    }

    /// <summary>The <c>~/.lunate</c> stand-in: the trust record, the lock and the build cache.</summary>
    internal string Store { get; }

    /// <summary>The session's working directory, so the file is discovered as a project extension.</summary>
    internal string Worktree { get; }

    internal string ExtensionsRoot { get; }

    /// <summary>Where the locked package and the reference pack are installed, NuGet's layout.</summary>
    internal string Packages { get; }

    internal SingleFileTarget Target { get; }

    internal ExtensionLockReferencePack ReferencePack { get; }

    /// <summary>The file the model writes: <c>&lt;worktree&gt;/.lunate/extensions/acme.cs</c>.</summary>
    internal string SourcePath => Path.Combine(ExtensionsRoot, "acme.cs");

    /// <summary>How often a restore engine was asked; zero is how "nothing reached a feed" reads.</summary>
    internal int Restores { get; private set; }

    /// <summary>
    /// How often the compile factory was called. The builder asks it only after trust and the
    /// cache answered, so an unchanged count is "the changed file was not compiled".
    /// </summary>
    internal int Compiles { get; private set; }

    internal static SingleFileLoopFixture Create()
    {
        if (!TryCreate(out SingleFileLoopFixture? fixture, out string? reason))
        {
            Assert.Skip(reason ?? "the reference pack is missing");
        }

        return fixture!;
    }

    /// <summary>
    /// The seams a session composes with: the temp store, the temp packages folder (so a load and
    /// its compile agree and neither reads the user's NuGet cache), the fixture engine and the
    /// real compiler behind counted factories.
    /// </summary>
    internal ExtensionSeams Seams(
        IExtensionTrustPrompt? prompt = null,
        RecordingExtensionLog? log = null,
        Action<string>? notice = null
    ) =>
        new()
        {
            StorePath = Store,
            GlobalPackagesFolder = Packages,
            Log = log ?? new RecordingExtensionLog(),
            Prompt = prompt,
            Notice = notice,
            RestoreEngine = () =>
            {
                Restores++;
                return new FixtureEngine(this);
            },
            Compiler = () =>
            {
                Compiles++;
                return new RoslynSingleFileCompiler();
            },
        };

    /// <summary>
    /// The source the model writes: one tool, one declared package, and a probe the tool answers
    /// with so a test can tell one build from the next. The literal is inserted verbatim.
    /// </summary>
    internal static string Source(string probeLiteral) =>
        Template.Replace("__PROBE__", probeLiteral, StringComparison.Ordinal);

    /// <summary>Writes the file and records the first-trust decision, the way an approving session does.</summary>
    internal string WriteTrusted(string source)
    {
        Directory.CreateDirectory(ExtensionsRoot);
        File.WriteAllText(SourcePath, source);
        ExtensionHeader header = ExtensionHeaderScanner.Scan(source);
        Assert.True(header.IsValid, "the fixture's header must be valid");
        new ExtensionTrustStore(
            new ExtensionHostOptions { StorePath = Store }
        ).RecordSingleFileTrust(
            SourcePath,
            SingleFileExtensionDigest.ComputeUnrestored(
                header.Id!,
                header.Packages,
                File.ReadAllBytes(SourcePath)
            )
        );
        return SourcePath;
    }

    /// <summary>The build service both the CLI and a reload use, over this fixture's seams.</summary>
    internal SingleFileExtensionBuilder Builder() =>
        new(
            new ExtensionHostOptions { StorePath = Store, GlobalPackagesFolder = Packages },
            () => new FixtureEngine(this),
            () => new RoslynSingleFileCompiler(),
            Target,
            Packages
        );

    public void Dispose() => _temp.Dispose();

    private const string Template = """
        #:extension acme-tools@1.0.0
        #:tool whoami
        #:package Acme.Tools@2.0.0

        using System.Text.Json;
        using System.Threading;
        using System.Threading.Tasks;
        using Lunate.Agent;
        using Lunate.Extensibility.Abstractions;

        public sealed class AcmeExtension : IExtensionFactory
        {
            public IExtension Create(IExtensionContext context)
            {
                context.RegisterTool(
                    new ExtensionToolRegistration
                    {
                        Name = "whoami",
                        Description = "answers with the package's greeting and this build's probe",
                        ParametersSchema = JsonDocument.Parse("{}").RootElement.Clone(),
                        Handler = new WhoAmI(),
                    }
                );
                return new Shell();
            }
        }

        internal sealed class WhoAmI : IExtensionToolHandler
        {
            public Task<ToolResult> InvokeAsync(
                JsonElement arguments,
                ToolContext context,
                CancellationToken ct
            ) => Task.FromResult(new ToolResult(Probe.Value, false));
        }

        internal sealed class Shell : IExtension;

        internal static class Probe
        {
            public static string Value =>
                global::Acme.Tools.Tools.Greeting() + ": " + __PROBE__;
        }
        """;

    /// <summary>
    /// The packages folder seeded with the reference pack and the compiled package: what a restore
    /// would have written, so the compile and the load read real bytes.
    /// </summary>
    private ExtensionLockPackage AddPackage(string packVersion)
    {
        Directory.CreateDirectory(Packages);
        string asset = FormattableString.Invariant($"lib/{Target.Tfm}/{PackageId}.dll");
        string directory = Path.Combine(
            Packages,
            PackageId.ToLowerInvariant(),
            PackageVersion.ToLowerInvariant(),
            "lib",
            Target.Tfm
        );
        Directory.CreateDirectory(directory);

        SingleFileCompileResult compiled = new RoslynSingleFileCompiler().Compile(
            new SingleFileCompileRequest
            {
                ExtensionId = PackageId,
                ExtensionVersion = PackageVersion,
                SourcePath = Path.Combine(_temp.Root, "Acme.Tools.cs"),
                SourceText = PackageSource,
                AssemblyName = PackageId,
                Target = Target,
                GlobalPackagesFolder = Packages,
                Lock = new ExtensionLock
                {
                    ExtensionId = PackageId,
                    ExtensionVersion = PackageVersion,
                    SourceDigest = "package",
                    PackagesWritten = [],
                    Toolset = new ExtensionLockToolset
                    {
                        Tfm = Target.Tfm,
                        Rid = Target.Rid,
                        Runtime = Target.Runtime,
                        ReferencePack = ReferencePack,
                    },
                    Sources = ["local"],
                    Packages = [],
                },
            },
            TestContext.Current.CancellationToken
        );
        Assert.True(compiled.Succeeded, "the fixture package did not compile: " + compiled.Reason);
        byte[] bytes = compiled.Assembly!;
        File.WriteAllBytes(Path.Combine(directory, PackageId + ".dll"), bytes);

        return new ExtensionLockPackage
        {
            Id = PackageId,
            Version = PackageVersion,
            Sha512 = Convert.ToBase64String(System.Security.Cryptography.SHA512.HashData(bytes)),
            Requested = FormattableString.Invariant($"{PackageId}@{PackageVersion}"),
            Direct = true,
            Files = [asset],
            Compile = [asset],
            Runtime = [asset],
            Native = [],
            Analyzers = [],
            Generators = false,
        };
    }

    /// <summary>
    /// The fixture, or a skip reason. The pack a restore for this runtime would pick is the exact
    /// runtime version when the machine has it, otherwise the lowest one at or above it — the same
    /// rule the product's reference-pack selection applies, so the fixture and the lock agree.
    /// </summary>
    private static bool TryCreate(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SingleFileLoopFixture? fixture,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? reason
    )
    {
        fixture = null;
        reason = null;

        List<(string Parent, string Version)> packs = [];
        foreach (string root in PackRoots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string directory in Directory.GetDirectories(root))
            {
                string name = Path.GetFileName(directory);
                if (
                    Version.TryParse(name, out _)
                    && Directory.Exists(Path.Combine(directory, "ref", "net10.0"))
                )
                {
                    packs.Add((root, name));
                }
            }
        }

        Version runtime = Version.Parse(SingleFileTarget.Current().Runtime);
        (string Parent, string Version)? chosen = packs.FirstOrDefault(pack =>
            Version.Parse(pack.Version) == runtime
        );
        chosen ??= packs
            .Where(pack => Version.Parse(pack.Version) >= runtime)
            .OrderBy(pack => Version.Parse(pack.Version))
            .Select(pack => ((string, string)?)pack)
            .FirstOrDefault();
        chosen ??= packs
            .OrderBy(pack => Version.Parse(pack.Version))
            .Select(pack => ((string, string)?)pack)
            .LastOrDefault();

        if (chosen is not { } pack)
        {
            reason =
                "no Microsoft.NETCore.App.Ref with ref/net10.0 was found in the NuGet cache or the "
                + "SDK's targeting packs, so a real compile cannot be exercised here";
            return false;
        }

        var temp = new TempDirectory();
        string packages = temp.File("packages");
        Directory.CreateDirectory(packages);
        string link = Path.Combine(packages, ReferencePackId.ToLowerInvariant());
        try
        {
            Directory.CreateSymbolicLink(link, pack.Parent);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CopyPack(Path.Combine(pack.Parent, pack.Version), link);
        }

        string refRoot = Path.Combine(
            link,
            pack.Version.ToLowerInvariant(),
            "ref",
            SingleFileTarget.Current().Tfm
        );
        var referencePack = new ExtensionLockReferencePack
        {
            Id = ReferencePackId,
            Version = pack.Version,
            Rule = ReferencePackRules.Exact,
            Assets =
            [
                .. Directory
                    .EnumerateFiles(refRoot, "*.dll")
                    .Select(file =>
                        FormattableString.Invariant(
                            $"ref/{SingleFileTarget.Current().Tfm}/{Path.GetFileName(file)}"
                        )
                    )
                    .Order(StringComparer.Ordinal),
            ],
        };

        fixture = new SingleFileLoopFixture(temp, packages, pack.Version, referencePack);
        return true;
    }

    /// <summary>Where a reference pack may live: NuGet's cache first, then the SDK's packs.</summary>
    private static IEnumerable<string> PackRoots()
    {
        string profile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolderOption.DoNotVerify
        );
        yield return Path.Combine(
            profile,
            ".nuget",
            "packages",
            ReferencePackId.ToLowerInvariant()
        );

        // shared/Microsoft.NETCore.App/<version>/System.Private.CoreLib.dll → the install root.
        string? runtime = Path.GetDirectoryName(typeof(object).Assembly.Location);
        for (int level = 0; level < 3 && runtime is not null; level++)
        {
            runtime = Path.GetDirectoryName(runtime);
        }

        if (runtime is not null)
        {
            yield return Path.Combine(runtime, "packs", ReferencePackId);
        }
    }

    private static void CopyPack(string source, string destination)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    /// <summary>
    /// The restore seam: answers with the fixture's one package and reference pack, and touches no
    /// feed. It counts its own requests, so a test can say nothing was restored.
    /// </summary>
    private sealed class FixtureEngine(SingleFileLoopFixture fixture) : ISingleFileRestoreEngine
    {
        public Task<SingleFileRestoreResult> RestoreAsync(
            SingleFileRestoreRequest request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            fixture.Restores++;
            return Task.FromResult(
                new SingleFileRestoreResult
                {
                    Packages = [fixture._package],
                    ReferencePack = fixture.ReferencePack,
                    Sources = ["local"],
                    GlobalPackagesFolder = fixture.Packages,
                }
            );
        }
    }
}
