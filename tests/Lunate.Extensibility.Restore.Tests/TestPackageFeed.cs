using System.Text;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// A flat local folder feed built for one test: real nupkgs with real nuspecs, lib/ref/analyzer and
/// native assets, written where the engine's NuGet client reads them. Nothing here talks to a
/// network, and the bytes differ per package and version, so a SHA-512 assertion means something.
/// </summary>
internal sealed class TestPackageFeed
{
    internal TestPackageFeed(string root)
    {
        Root = root;
        Directory.CreateDirectory(FeedDirectory);
    }

    internal string Root { get; }

    internal string FeedDirectory => Path.Combine(Root, "feed");

    internal string GlobalPackages => Path.Combine(Root, "packages");

    internal SingleFilePackageSource Source => new("local", FeedDirectory);

    /// <summary>
    /// Adds <c>&lt;id&gt;.&lt;version&gt;.nupkg</c>. A dependency string is <c>Id@version</c>; the
    /// range becomes <c>[version, )</c>, which is what a real package's nuspec carries.
    /// </summary>
    internal TestPackageFeed Add(
        string id,
        string version,
        string[]? dependencies = null,
        bool analyzers = false,
        bool referenceAssemblies = false,
        string? nativeRid = null,
        string? tfm = null
    )
    {
        string framework = tfm ?? "net10.0";
        var builder = new PackageBuilder
        {
            Id = id,
            Version = NuGetVersion.Parse(version),
            Description = id,
        };
        builder.Authors.Add("lunate-tests");

        if (dependencies is { Length: > 0 })
        {
            builder.DependencyGroups.Add(
                new PackageDependencyGroup(
                    NuGetFramework.Parse(framework),
                    [
                        .. dependencies.Select(dependency => new PackageDependency(
                            dependency.Split('@')[0],
                            VersionRange.Parse(
                                FormattableString.Invariant($"[{dependency.Split('@')[1]}, )")
                            )
                        )),
                    ]
                )
            );
        }

        Add(builder, $"lib/{framework}/{id}.dll", Payload(id, version, "lib"));
        if (referenceAssemblies)
        {
            Add(builder, $"ref/{framework}/{id}.dll", Payload(id, version, "ref"));
        }

        if (analyzers)
        {
            Add(
                builder,
                $"analyzers/dotnet/cs/{id}.Analyzers.dll",
                Payload(id, version, "analyzer")
            );
        }

        if (nativeRid is { Length: > 0 })
        {
            Add(
                builder,
                $"runtimes/{nativeRid}/native/lib{id}.dylib",
                Payload(id, version, "native")
            );
        }

        string path = System.IO.Path.Combine(FeedDirectory, $"{id}.{version}.nupkg");
        using FileStream stream = File.Create(path);
        builder.Save(stream);
        return this;
    }

    /// <summary>
    /// The reference pack a compile resolves against: <c>ref/net10.0</c> assemblies, which is the
    /// only folder <see cref="NuGetReferencePackRestore"/> looks for.
    /// </summary>
    internal TestPackageFeed AddReferencePack(string version, params string[] assemblies)
    {
        string[] names = assemblies.Length == 0 ? ["System.Runtime.dll"] : assemblies;
        var builder = new PackageBuilder
        {
            Id = "Microsoft.NETCore.App.Ref",
            Version = NuGetVersion.Parse(version),
            Description = "reference pack",
        };
        builder.Authors.Add("lunate-tests");
        foreach (string name in names)
        {
            Add(
                builder,
                $"ref/net10.0/{name}",
                Payload("Microsoft.NETCore.App.Ref", version, name)
            );
        }

        using FileStream stream = File.Create(
            System.IO.Path.Combine(FeedDirectory, $"Microsoft.NETCore.App.Ref.{version}.nupkg")
        );
        builder.Save(stream);
        return this;
    }

    /// <summary>The SHA-512 NuGet records for a package in this feed, base64.</summary>
    internal string Sha512(string id, string version) =>
        Convert.ToBase64String(
            System.Security.Cryptography.SHA512.HashData(
                File.ReadAllBytes(Path.Combine(FeedDirectory, $"{id}.{version}.nupkg"))
            )
        );

    /// <summary>The nupkg's version folder in the global packages folder, NuGet's lowercase layout.</summary>
    internal string InstallPath(string id, string version) =>
        Path.Combine(GlobalPackages, id.ToLowerInvariant(), version.ToLowerInvariant());

    private static void Add(PackageBuilder builder, string targetPath, string content)
    {
        string sourcePath = Path.Combine(
            Path.GetTempPath(),
            string.Concat("lunate-pkg-", Guid.NewGuid().ToString("N"), ".bin")
        );
        File.WriteAllText(sourcePath, content, new UTF8Encoding(false));
        builder.Files.Add(
            new PhysicalPackageFile { SourcePath = sourcePath, TargetPath = targetPath }
        );
    }

    private static string Payload(string id, string version, string kind) =>
        $"{kind} bytes of {id} {version} for the Lunate restore tests";
}
