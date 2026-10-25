using System.Runtime.InteropServices;
using Lunate.Extensibility;
using Lunate.Extensibility.Compile;

namespace Lunate.Coding.Tests;

/// <summary>
/// The pack half of <see cref="SingleFileLoopFixture"/>: where the reference assemblies come from,
/// the packages folder laid out the way a restore writes it, and the one package compiled against
/// that pack — the bytes the extension's compile and its load both read. The pack is taken from the
/// NuGet cache when one is there, otherwise from the SDK's targeting packs; neither is built by the
/// fixture, so the compiler is held to what the machine actually ships. A machine with neither
/// skips the family with the reason, because a compile against a fabricated pack proves nothing.
/// </summary>
internal sealed partial class SingleFileLoopFixture
{
    private const string ReferencePackId = "Microsoft.NETCore.App.Ref";

    internal static SingleFileLoopFixture Create()
    {
        if (!TryCreate(out SingleFileLoopFixture? fixture, out string? reason))
        {
            Assert.Skip(reason ?? "the reference pack is missing");
        }

        return fixture!;
    }

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

        string tfm = SingleFileTarget.Current().Tfm;
        string refRoot = Path.Combine(link, pack.Version.ToLowerInvariant(), "ref", tfm);
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
                        FormattableString.Invariant($"ref/{tfm}/{Path.GetFileName(file)}")
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
