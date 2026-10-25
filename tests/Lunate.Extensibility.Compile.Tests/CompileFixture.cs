using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Lunate.Extensibility;
using Lunate.Extensibility.Compile;

namespace Lunate.Extensibility.Compile.Tests;

/// <summary>A scratch root for one test, removed on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    internal TempDirectory()
    {
        Root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "lunate-compile-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Root);
    }

    internal string Root { get; }

    internal string Path(string name) => System.IO.Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// The real reference assemblies of this machine, as a compile needs them: a packages folder laid
/// out the way a restore writes it, and the version to name in a lock. The pack is taken from the
/// NuGet cache when a restore has already put it there, otherwise from the SDK's targeting pack;
/// neither is built by this fixture, so the compiler is held to the assembles the runtime actually
/// ships. A machine with neither skips the test family with the reason, because a compile against a
/// fabricated pack would prove nothing.
/// </summary>
internal sealed class CompileFixture : IDisposable
{
    private readonly TempDirectory _temp;

    /// <summary>The reference pack's package id; the restore engine resolves the same one.</summary>
    private const string ReferencePackId = "Microsoft.NETCore.App.Ref";

    private CompileFixture(TempDirectory temp, string packagesFolder, string packVersion)
    {
        _temp = temp;
        PackagesFolder = packagesFolder;
        RefPackVersion = packVersion;
        Target = SingleFileTarget.Of(
            Version.Parse(packVersion),
            RuntimeInformation.RuntimeIdentifier
        );
        ReferencePack = new ExtensionLockReferencePack
        {
            Id = ReferencePackId,
            Version = packVersion,
            Rule = ReferencePackRules.Exact,
            Assets =
            [
                .. Directory
                    .EnumerateFiles(
                        Path.Combine(
                            packagesFolder,
                            ReferencePackId.ToLowerInvariant(),
                            packVersion.ToLowerInvariant(),
                            "ref",
                            Target.Tfm
                        ),
                        "*.dll"
                    )
                    .Select(file => $"ref/{Target.Tfm}/{Path.GetFileName(file)}")
                    .Order(StringComparer.Ordinal),
            ],
        };
        Store = Path.Combine(temp.Root, "store");
        Extensions = Path.Combine(Store, "extensions");
        Directory.CreateDirectory(Extensions);
    }

    /// <summary>Where the locked packages and the reference pack live, NuGet's layout.</summary>
    internal string PackagesFolder { get; }

    internal string RefPackVersion { get; }

    internal SingleFileTarget Target { get; }

    internal ExtensionLockReferencePack ReferencePack { get; }

    /// <summary>The <c>~/.lunate</c> stand-in: the trust record and the cache live here.</summary>
    internal string Store { get; }

    /// <summary>The global extensions root, so a file written here is discovered as a user extension.</summary>
    internal string Extensions { get; }

    internal ExtensionHostOptions Options() => new() { StorePath = Store };

    /// <summary>
    /// The fixture, or a skip reason. Every test family starts here so a machine without reference
    /// assemblies reports why instead of pretending a compile was verified.
    /// </summary>
    internal static CompileFixture Create()
    {
        if (!TryCreate(out CompileFixture? fixture, out string? reason))
        {
            Assert.Skip(reason ?? "the reference pack is missing");
        }

        return fixture!;
    }

    internal static bool TryCreate(
        [NotNullWhen(true)] out CompileFixture? fixture,
        [NotNullWhen(false)] out string? reason
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
                    Version.TryParse(name, out Version? parsed)
                    && Directory.Exists(Path.Combine(directory, "ref", "net10.0"))
                )
                {
                    packs.Add((root, name));
                }
            }
        }

        // The pack a restore for this runtime would pick: the exact runtime version when it exists,
        // otherwise the lowest one at or above it (the same rule ReferencePackRule applies), so the
        // fixture and the product's engine agree on the toolset.
        (string Parent, string Version)? chosen = null;
        Version runtime = Version.Parse(SingleFileTarget.Current().Runtime);
        foreach ((string parent, string version) in packs)
        {
            Version parsed = Version.Parse(version);
            if (parsed != runtime)
            {
                continue;
            }

            chosen = (parent, version);
            break;
        }

        chosen ??= packs
            .Where(pack => Version.Parse(pack.Version) >= runtime)
            .OrderBy(pack => Version.Parse(pack.Version))
            .Select(pack => ((string, string)?)pack)
            .FirstOrDefault();
        chosen ??= packs
            .OrderBy(pack => Version.Parse(pack.Version))
            .Select(pack => ((string, string)?)pack)
            .LastOrDefault();

        string? chosenParent = chosen?.Parent;
        string? chosenVersion = chosen?.Version;

        if (chosenParent is null || chosenVersion is null)
        {
            reason =
                "no Microsoft.NETCore.App.Ref with ref/net10.0 was found in the NuGet cache or the "
                + "SDK's targeting packs, so a real compile cannot be exercised here";
            return false;
        }

        var temp = new TempDirectory();
        string packages = Path.Combine(temp.Root, "packages");
        Directory.CreateDirectory(packages);
        string packLink = Path.Combine(packages, ReferencePackId.ToLowerInvariant());
        try
        {
            Directory.CreateSymbolicLink(packLink, chosenParent);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CopyPack(chosenParent, chosenVersion, packLink);
        }

        fixture = new CompileFixture(temp, packages, chosenVersion);
        return true;
    }

    /// <summary>
    /// Compiles <paramref name="code"/> as one locked package's <c>lib/tfm</c> assembly and installs
    /// it in the packages folder, so an extension source can call into it. The library is compiled
    /// by the compiler under test — the same route a real package's assembly takes — and its
    /// reference pack is the fixture's.
    /// </summary>
    internal ExtensionLockPackage AddPackage(
        string id,
        string version,
        string code,
        CancellationToken cancellationToken
    )
    {
        string asset = FormattableString.Invariant($"lib/{Target.Tfm}/{id}.dll");
        string directory = Path.Combine(
            PackagesFolder,
            id.ToLowerInvariant(),
            version.ToLowerInvariant(),
            "lib",
            Target.Tfm
        );
        Directory.CreateDirectory(directory);
        byte[] assembly = Assembly(
            Compile(
                new SingleFileCompileRequest
                {
                    ExtensionId = id,
                    ExtensionVersion = version,
                    SourcePath = Path.Combine(_temp.Root, id + ".cs"),
                    SourceText = code,
                    AssemblyName = id,
                    Target = Target,
                    GlobalPackagesFolder = PackagesFolder,
                    Lock = Lock(),
                },
                cancellationToken
            )
        );
        File.WriteAllBytes(Path.Combine(directory, id + ".dll"), assembly);

        return new ExtensionLockPackage
        {
            Id = id,
            Version = version,
            Sha512 = Convert.ToBase64String(System.Security.Cryptography.SHA512.HashData(assembly)),
            Requested = FormattableString.Invariant($"{id}@{version}"),
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
    /// Roslyn itself as a locked package: a source generator's own dependency, which has to be in the
    /// lock for the generator to compile at all. The package folder gets the assembly this process
    /// runs, so the generator binds to the same Roslyn the compile under test uses.
    /// </summary>
    internal ExtensionLockPackage AddRoslynPackage()
    {
        const string Id = "Microsoft.CodeAnalysis";
        string version = typeof(Microsoft.CodeAnalysis.Compilation)
            .Assembly.GetName()
            .Version!.ToString(3);
        string asset = FormattableString.Invariant($"lib/{Target.Tfm}/{Id}.dll");
        string directory = Path.Combine(
            PackagesFolder,
            Id.ToLowerInvariant(),
            version.ToLowerInvariant(),
            "lib",
            Target.Tfm
        );
        Directory.CreateDirectory(directory);
        byte[] image = File.ReadAllBytes(
            typeof(Microsoft.CodeAnalysis.Compilation).Assembly.Location
        );
        File.WriteAllBytes(Path.Combine(directory, Id + ".dll"), image);

        return new ExtensionLockPackage
        {
            Id = Id,
            Version = version,
            Sha512 = Convert.ToBase64String(System.Security.Cryptography.SHA512.HashData(image)),
            Requested = FormattableString.Invariant($"{Id}@{version}"),
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
    /// A locked package that ships build-time code: the assembly compiled from
    /// <paramref name="code"/> is installed under <c>analyzers/dotnet/cs</c> and recorded in the lock
    /// the way a restore records it. The package contributes no compile assets — an extension does
    /// not reference a generator, it runs one — so the only thing that reads the assembly is the
    /// compile's opt-in path.
    /// </summary>
    internal ExtensionLockPackage AddAnalyzerPackage(
        string id,
        string version,
        string code,
        CancellationToken cancellationToken
    )
    {
        string name = id + ".dll";
        string asset = FormattableString.Invariant($"analyzers/dotnet/cs/{name}");
        string directory = Path.Combine(
            PackagesFolder,
            id.ToLowerInvariant(),
            version.ToLowerInvariant(),
            "analyzers",
            "dotnet",
            "cs"
        );
        Directory.CreateDirectory(directory);
        byte[] image = Assembly(
            Compile(
                new SingleFileCompileRequest
                {
                    ExtensionId = id,
                    ExtensionVersion = version,
                    SourcePath = Path.Combine(_temp.Root, id + ".cs"),
                    SourceText = code,
                    AssemblyName = id,
                    Target = Target,
                    GlobalPackagesFolder = PackagesFolder,
                    Lock = Lock(AddRoslynPackage()),
                },
                cancellationToken
            )
        );
        File.WriteAllBytes(Path.Combine(directory, name), image);

        return new ExtensionLockPackage
        {
            Id = id,
            Version = version,
            Sha512 = Convert.ToBase64String(System.Security.Cryptography.SHA512.HashData(image)),
            Requested = FormattableString.Invariant($"{id}@{version}"),
            Direct = true,
            Files = [asset],
            Compile = [],
            Runtime = [],
            Native = [],
            Analyzers = [asset],
            Generators = true,
        };
    }

    /// <summary>
    /// A lock over the reference pack and the given packages, for a direct compile.
    /// </summary>
    internal ExtensionLock Lock(params ExtensionLockPackage[] packages) =>
        new()
        {
            ExtensionId = "acme-tools",
            ExtensionVersion = "1.0.0",
            SourceDigest = "source",
            PackagesWritten = [.. packages.Select(package => package.Requested)],
            Toolset = new ExtensionLockToolset
            {
                Tfm = Target.Tfm,
                Rid = Target.Rid,
                Runtime = Target.Runtime,
                ReferencePack = ReferencePack,
            },
            Sources = ["local"],
            Packages = packages,
        };

    /// <summary>One direct compile against the fixture's pack and the given packages.</summary>
    internal SingleFileCompileResult Compile(
        SingleFileCompileRequest request,
        CancellationToken cancellationToken
    ) => new RoslynSingleFileCompiler().Compile(request, cancellationToken);

    /// <summary>Asserts a compile succeeded and returns its assembly.</summary>
    internal static byte[] Assembly(SingleFileCompileResult result)
    {
        Assert.True(result.Succeeded, "the compile failed: " + result.Reason);
        return result.Assembly!;
    }

    /// <summary>
    /// Writes one extension source under the fixture's extensions root and records the first-trust
    /// decision for it, the way an approving session does — the CLI must never approve on its own.
    /// </summary>
    internal string WriteTrustedFile(string name, string source)
    {
        Directory.CreateDirectory(Extensions);
        string path = Path.Combine(Extensions, name);
        File.WriteAllText(path, source);

        ExtensionHeader header = ExtensionHeaderScanner.Scan(source);
        Assert.True(header.IsValid, "the fixture's header must be valid");
        string digest = SingleFileExtensionDigest.ComputeUnrestored(
            header.Id!,
            header.Packages,
            File.ReadAllBytes(path)
        );
        new ExtensionTrustStore(Options()).RecordSingleFileTrust(path, digest);
        return path;
    }

    public void Dispose() => _temp.Dispose();

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

    private static void CopyPack(string parent, string version, string destination)
    {
        string source = Path.Combine(parent, version);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
