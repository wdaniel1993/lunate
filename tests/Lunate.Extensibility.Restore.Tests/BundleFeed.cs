using System.Security.Cryptography;
using NuGet.Versioning;

namespace Lunate.Extensibility.Restore.Tests;

/// <summary>
/// One bundle feed for a test: nupkgs built by <see cref="TestPackage"/> in a local folder, a
/// packages folder of its own, and the installer wired to both. Nothing here reaches a network, and
/// every hash assertion is about bytes the test itself wrote.
/// </summary>
internal sealed class BundleFeed
{
    internal BundleFeed(string root)
    {
        Root = root;
        Directory.CreateDirectory(FeedDirectory);
    }

    internal string Root { get; }

    internal string FeedDirectory => Path.Combine(Root, "feed");

    internal string GlobalPackages => Path.Combine(Root, "packages");

    internal SingleFilePackageSource Source => new("local", FeedDirectory);

    /// <summary>A bundle under construction, saved into this feed by <see cref="TestPackage.Save"/>.</summary>
    internal TestPackage Bundle(string id = "Acme.Toolkit", string version = "1.4.2") =>
        new(Root, id, version);

    /// <summary>The installer, wired to this feed, its packages folder and an observer that counts calls.</summary>
    internal PackageInstaller Installer(
        Action<string>? observer = null,
        bool offline = false,
        DateTimeOffset? now = null
    ) =>
        new(
            new PackageInstallOptions
            {
                Sources = [Source],
                GlobalPackagesFolder = GlobalPackages,
                OnFeedRequest = observer,
                Offline = offline,
                Clock = now is { } instant ? new FixedTime(instant) : TimeProvider.System,
            }
        );

    /// <summary>The nupkg as the packages folder holds it, NuGet's lowercase layout.</summary>
    internal string CachedNupkg(string id, string version) =>
        Path.Combine(
            GlobalPackages,
            id.ToLowerInvariant(),
            version.ToLowerInvariant(),
            FormattableString.Invariant(
                $"{id.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg"
            )
        );

    /// <summary>The <c>.nupkg.sha512</c> file NuGet writes beside a package it installed.</summary>
    internal string CachedHash(string id, string version) => CachedNupkg(id, version) + ".sha512";

    internal string Sha512(string id, string version) =>
        Convert.ToBase64String(
            SHA512.HashData(File.ReadAllBytes(Path.Combine(FeedDirectory, $"{id}.{version}.nupkg")))
        );

    internal static string Sha256(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class FixedTime(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}

/// <summary>The SHA-512 of a file's bytes, base64 — what NuGet records for a package.</summary>
internal static class FileHashes
{
    internal static string Sha512(string path) =>
        Convert.ToBase64String(SHA512.HashData(File.ReadAllBytes(path)));
}
