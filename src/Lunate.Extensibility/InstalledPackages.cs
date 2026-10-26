using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lunate.Ai;

namespace Lunate.Extensibility;

/// <summary>
/// One installed package as the trust machinery needs it (design D7): the identity the user chose
/// — canonical id, exact version and the nupkg's SHA-512 — the feeds it came from, what it provides
/// and where its record says it landed. An install's record is the source of truth; this is a
/// tolerant read of it, so a record that does not answer for itself contributes nothing and the
/// content it names stays untrusted.
/// </summary>
internal sealed record InstalledPackageProvenance
{
    internal required string PackageId { get; init; }

    internal required string Version { get; init; }

    internal required string Sha512 { get; init; }

    internal required IReadOnlyList<string> Sources { get; init; }

    /// <summary>The packaged extension's id; null for a skills-only package.</summary>
    internal string? ExtensionId { get; init; }

    internal required IReadOnlyList<string> Skills { get; init; }

    internal bool IsProject { get; init; }

    /// <summary>The worktree a project install belongs to; null for a user install.</summary>
    internal string? Worktree { get; init; }

    internal required string RecordPath { get; init; }

    /// <summary>The first feed the package came from, for the one-line provenance.</summary>
    internal string Source => Sources.Count > 0 ? Sources[0] : "unknown";

    /// <summary><c>&lt;id&gt;@&lt;version&gt;</c>, the form <c>/skills</c> shows.</summary>
    internal string PackageText =>
        string.Create(CultureInfo.InvariantCulture, $"{PackageId}@{Version}");

    /// <summary>The trust question's one line: <c>from package &lt;id&gt;@&lt;version&gt; (&lt;source&gt;)</c>.</summary>
    internal string QuestionLine =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"from package {PackageId}@{Version} ({Source})"
        );
}

/// <summary>
/// One install record that cannot be believed (design D8): the record's path, the package directory
/// it sits in (the only name left when the document cannot be read) and the parser's one-line
/// reason. A broken record is a fact the trust boundary acts on, not a record it ignores.
/// </summary>
internal sealed record BrokenInstallRecord(
    string RecordPath,
    string PackageDirectory,
    string Reason
);

/// <summary>
/// What this machine has installed, read from the records under <c>&lt;root&gt;/packages/</c>
/// (design D7, D8). It answers two questions the rest of the host asks: which package provided a
/// directory that discovery found, and what digest a trust decision has to bind so an upgraded
/// package re-prompts exactly as changed files do.
/// <para>
/// A record is machine state, not repository content, so a project install of another worktree is
/// simply not about the directory in front of us: matching is by the path the record's scope and
/// worktree imply. A record that cannot be read, or whose integrity hash does not answer for its
/// bytes, is collected as <see cref="Broken"/> rather than dropped: a load must never mistake a
/// damaged record for a hand-placed extension.
/// </para>
/// </summary>
internal sealed class InstalledPackages
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsLinux()
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;

    private readonly string _storeRoot;
    private readonly IReadOnlyList<InstalledPackageProvenance> _all;
    private readonly IReadOnlyList<BrokenInstallRecord> _broken;

    private InstalledPackages(
        string storeRoot,
        IReadOnlyList<InstalledPackageProvenance> all,
        IReadOnlyList<BrokenInstallRecord> broken
    )
    {
        _storeRoot = storeRoot;
        _all = all;
        _broken = broken;
    }

    /// <summary>No installs: what a store without records answers.</summary>
    internal static InstalledPackages Empty { get; } = new(string.Empty, [], []);

    internal IReadOnlyList<InstalledPackageProvenance> All => _all;

    /// <summary>
    /// Every record under the store that cannot be read or no longer answers for itself, ordered by
    /// package directory. Readable records are not here; a missing record file names nothing and is
    /// not a broken record either.
    /// </summary>
    internal IReadOnlyList<BrokenInstallRecord> Broken => _broken;

    /// <summary>
    /// Every readable record under <paramref name="storeRoot"/>, ordered by package id, and every
    /// record that cannot be believed. A missing store or an unreadable directory yields neither.
    /// </summary>
    internal static InstalledPackages Read(string storeRoot)
    {
        if (string.IsNullOrWhiteSpace(storeRoot))
        {
            return Empty;
        }

        string packages = Path.Combine(storeRoot, "packages");
        List<InstalledPackageProvenance> found = [];
        List<BrokenInstallRecord> broken = [];
        if (!Directory.Exists(packages))
        {
            return new InstalledPackages(storeRoot, found, broken);
        }

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(packages).Order(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new InstalledPackages(storeRoot, found, broken);
        }

        foreach (string directory in directories)
        {
            string path = Path.Combine(directory, PackageInstallRecordJson.FileName);
            if (!File.Exists(path))
            {
                // A directory without a record is a half-cleaned install: it names no content.
                continue;
            }

            PackageInstallRecord? record = PackageInstallRecordJson.TryRead(
                path,
                out string? problem
            );
            if (record is null)
            {
                broken.Add(
                    new BrokenInstallRecord(
                        path,
                        Path.GetFileName(directory),
                        problem ?? "the record could not be read"
                    )
                );
                continue;
            }

            found.Add(
                new InstalledPackageProvenance
                {
                    PackageId = record.Package.Id,
                    Version = record.Package.Version,
                    Sha512 = record.Package.Sha512,
                    Sources = record.Package.Sources,
                    ExtensionId = record.Content.ExtensionId,
                    Skills = record.Content.Skills,
                    IsProject = record.Scope is PackageScope.Project,
                    Worktree = record.Worktree,
                    RecordPath = path,
                }
            );
        }

        found.Sort(
            (left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(left.PackageId, right.PackageId)
        );
        return new InstalledPackages(storeRoot, found, broken);
    }

    /// <summary>The install that provides an extension directory, or null when it is hand-placed.</summary>
    internal InstalledPackageProvenance? ForExtension(string directory) =>
        _all.FirstOrDefault(provenance =>
            provenance.ExtensionId is { Length: > 0 } id
            && SamePath(directory, ExtensionDirectory(provenance, id))
        );

    /// <summary>The install that provides a skill directory, or null when it is hand-written.</summary>
    internal InstalledPackageProvenance? ForSkill(string directory) =>
        _all.FirstOrDefault(provenance =>
            provenance.Skills.Any(name => SamePath(directory, SkillDirectory(provenance, name)))
        );

    /// <summary>
    /// The trust digest a decision records for content that came from a package (design D7): the
    /// directory content hash, the identity the user chose, and the canonical graph of the
    /// resolution the install wrote, so an upgraded package — or the same package whose dependencies
    /// now resolve differently — changes the digest and asks again, while an identical reinstall
    /// keeps the decision. The one marker keeps this family disjoint from a plain content hash.
    /// </summary>
    internal string BindTrustDigest(string contentHash, InstalledPackageProvenance? provenance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        if (provenance is null)
        {
            return contentHash;
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (
            string part in new[]
            {
                "package",
                provenance.PackageId,
                provenance.Version,
                provenance.Sha512,
                contentHash,
            }
        )
        {
            hash.AppendData(Encoding.UTF8.GetBytes(part));
            hash.AppendData([0]);
        }

        // The resolution's graph, not the toolset half: a .NET patch must never ask the user about
        // bytes they already approved, and a dependency that resolved elsewhere must.
        byte[] graph = LockGraph(provenance);
        hash.AppendData(graph);
        hash.AppendData([0]);

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// The canonical graph bytes of the lock an install wrote for this package's extension, or empty
    /// when the package carries no extension or its lock is gone — so a record without a lock binds
    /// the same bytes it did before the lock existed instead of failing the load.
    /// </summary>
    private byte[] LockGraph(InstalledPackageProvenance provenance)
    {
        if (provenance.ExtensionId is not { Length: > 0 } extensionId)
        {
            return [];
        }

        string path = SingleFileExtensionPaths.LockPath(_storeRoot, extensionId);
        try
        {
            return File.Exists(path)
                ? ExtensionLockJson.Read(File.ReadAllText(path)).GraphBytes
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private string ExtensionDirectory(InstalledPackageProvenance provenance, string id) =>
        provenance.IsProject
            ? ProjectScope.Under(
                provenance.Worktree ?? string.Empty,
                ProjectScope.ExtensionsDirectory,
                id
            )
            : Path.Combine(_storeRoot, "extensions", id);

    private string SkillDirectory(InstalledPackageProvenance provenance, string name) =>
        provenance.IsProject
            ? ProjectScope.Under(
                provenance.Worktree ?? string.Empty,
                ProjectScope.SkillsDirectory,
                name
            )
            : Path.Combine(_storeRoot, "skills", name);

    /// <summary>
    /// Two paths naming one place, canonicalized the way discovery canonicalizes (existing symlink
    /// components resolved), so a record written under one spelling of a temp path still matches the
    /// directory a scan reports. A path this platform cannot resolve is never a match.
    /// </summary>
    private static bool SamePath(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                PathCanonical.Canonicalize(left),
                PathCanonical.Canonicalize(right),
                PathComparison
            );
        }
        catch (Exception exception)
            when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }
}
