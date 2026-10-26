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
/// What this machine has installed, read from the records under <c>&lt;root&gt;/packages/</c>
/// (design D7, D8). It answers two questions the rest of the host asks: which package provided a
/// directory that discovery found, and what digest a trust decision has to bind so an upgraded
/// package re-prompts exactly as changed files do.
/// <para>
/// A record is machine state, not repository content, so a project install of another worktree is
/// simply not about the directory in front of us: matching is by the path the record's scope and
/// worktree imply, and a record that cannot be read contributes nothing rather than a guess.
/// </para>
/// </summary>
internal sealed class InstalledPackages
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsLinux()
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;

    private readonly string _storeRoot;
    private readonly IReadOnlyList<InstalledPackageProvenance> _all;

    private InstalledPackages(string storeRoot, IReadOnlyList<InstalledPackageProvenance> all)
    {
        _storeRoot = storeRoot;
        _all = all;
    }

    /// <summary>No installs: what a store without records answers.</summary>
    internal static InstalledPackages Empty { get; } = new(string.Empty, []);

    internal IReadOnlyList<InstalledPackageProvenance> All => _all;

    /// <summary>
    /// Every readable record under <paramref name="storeRoot"/>, ordered by package id. A missing
    /// store, an unreadable directory and a malformed record each contribute no entry.
    /// </summary>
    internal static InstalledPackages Read(string storeRoot)
    {
        if (string.IsNullOrWhiteSpace(storeRoot))
        {
            return Empty;
        }

        string packages = Path.Combine(storeRoot, "packages");
        List<InstalledPackageProvenance> found = [];
        if (!Directory.Exists(packages))
        {
            return new InstalledPackages(storeRoot, found);
        }

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(packages).Order(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new InstalledPackages(storeRoot, found);
        }

        foreach (string directory in directories)
        {
            string path = Path.Combine(directory, "install.json");
            if (TryRead(path, out InstalledPackageProvenance? provenance))
            {
                found.Add(provenance!);
            }
        }

        found.Sort(
            (left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(left.PackageId, right.PackageId)
        );
        return new InstalledPackages(storeRoot, found);
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
    /// directory content hash plus the identity the user chose, so an upgraded package changes the
    /// digest and asks again, while an identical reinstall keeps the decision. The one marker keeps
    /// this family disjoint from a plain content hash. The canonical lock graph joins in a later
    /// phase through this same function.
    /// </summary>
    internal static string BindTrustDigest(
        string contentHash,
        InstalledPackageProvenance? provenance
    )
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

        return Convert.ToHexStringLower(hash.GetHashAndReset());
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

    private static bool TryRead(string path, out InstalledPackageProvenance? provenance)
    {
        provenance = null;
        string text;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            text = reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            if (
                root.ValueKind != JsonValueKind.Object
                || root.TryGetProperty("package", out JsonElement package) is false
                || package.ValueKind != JsonValueKind.Object
                || Text(package, "id") is not { Length: > 0 } id
                || Text(package, "version") is not { Length: > 0 } version
                || Text(package, "sha512") is not { Length: > 0 } sha512
                || root.TryGetProperty("content", out JsonElement content) is false
                || content.ValueKind != JsonValueKind.Object
            )
            {
                return false;
            }

            provenance = new InstalledPackageProvenance
            {
                PackageId = id,
                Version = version,
                Sha512 = sha512,
                Sources = Strings(package, "sources"),
                ExtensionId =
                    content.TryGetProperty("extension", out JsonElement extension)
                    && extension.ValueKind == JsonValueKind.Object
                        ? Text(extension, "id")
                        : null,
                Skills = Strings(content, "skills"),
                IsProject = string.Equals(Text(root, "scope"), "project", StringComparison.Ordinal),
                Worktree = Text(root, "worktree"),
                RecordPath = path,
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? Text(JsonElement node, string key) =>
        node.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> Strings(JsonElement node, string key)
    {
        if (
            !node.TryGetProperty(key, out JsonElement value)
            || value.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        return
        [
            .. value
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .Where(item => item.Length > 0),
        ];
    }

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
