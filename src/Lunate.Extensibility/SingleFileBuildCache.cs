using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunate.Extensibility;

/// <summary>
/// The build cache key: <c>sha256(sourceHash | lockHash | apiVersion | TFM | RID)</c>, fields
/// NUL-separated and hashed in that order, rendered as lowercase hex and truncated to the first
/// <see cref="Length"/> characters. It is a content address, not a name: the same source, the same
/// lock (both halves, so a graph change and a toolset change each move it), the same host contract
/// version and the same toolset always produce the same key, which is what makes a rebuild a cache
/// hit instead of a second artifact. Truncation is safe here because the key names a directory under
/// the user's own cache and the manifest inside it records the untruncated hashes.
/// </summary>
public static class SingleFileBuildKey
{
    /// <summary>How many leading hex characters of the SHA-256 the key keeps.</summary>
    public const int Length = 16;

    /// <summary>The two toolset fields and three hashes/versions that identify a build.</summary>
    public static string Compute(
        string sourceHash,
        string lockHash,
        string apiVersion,
        string tfm,
        string rid
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(tfm);
        ArgumentException.ThrowIfNullOrWhiteSpace(rid);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] separator = [0];
        foreach (string field in new[] { sourceHash, lockHash, apiVersion, tfm, rid })
        {
            hash.AppendData(Encoding.UTF8.GetBytes(field));
            hash.AppendData(separator);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset())[..Length];
    }

    /// <summary>True when <paramref name="value"/> has the shape <see cref="Compute"/> produces.</summary>
    public static bool IsKey(string? value) =>
        value is { Length: Length }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// <summary>
/// One locked package as the build's manifest records it for a load: the exact version and the
/// package-relative runtime and native assets phase 3 resolves out of the global packages folder.
/// Compile-only assets are not here — they shaped the assembly and are not loaded.
/// </summary>
public sealed record SingleFileBuildPackage
{
    public required string Id { get; init; }

    public required string Version { get; init; }

    public IReadOnlyList<string> Runtime { get; init; } = [];

    public IReadOnlyList<string> Native { get; init; } = [];
}

/// <summary>
/// <c>builds/&lt;key&gt;/build.json</c>: what one compiled assembly was built from and what it needs
/// at load. It repeats the key's inputs in full (untruncated hashes included) so a build directory
/// answers for itself without the lock that produced it, and it lists the dependency resolution —
/// per package its runtime and native assets — so phase 3 loads a build without re-resolving or
/// reading a lock that may since have been rewritten. Every path in it is package-relative, so the
/// manifest is machine-independent.
/// </summary>
public sealed record SingleFileBuildManifest
{
    /// <summary>The build layout this manifest writes and reads; another value is refused.</summary>
    public const int CurrentVersion = 1;

    public const string FileName = "build.json";

    public int Version { get; init; } = CurrentVersion;

    /// <summary>The truncated cache key: the directory's own name.</summary>
    public required string Key { get; init; }

    public required string Assembly { get; init; }

    public required string ExtensionId { get; init; }

    public required string ExtensionVersion { get; init; }

    /// <summary>The host contract version the assembly was compiled against.</summary>
    public required string ApiVersion { get; init; }

    public required string Tfm { get; init; }

    public required string Rid { get; init; }

    /// <summary>The source-level digest: the id, the declared packages and the source bytes.</summary>
    public required string SourceHash { get; init; }

    /// <summary>The SHA-256 of the whole lock file, both halves included.</summary>
    public required string LockHash { get; init; }

    /// <summary>The resolution the assembly needs at load, ordered by id.</summary>
    public IReadOnlyList<SingleFileBuildPackage> Packages { get; init; } = [];
}

/// <summary>One build on disk: its key, where it lives and the manifest it carries.</summary>
public sealed record SingleFileBuild
{
    public required string Key { get; init; }

    public required string ExtensionId { get; init; }

    public required string BuildPath { get; init; }

    public required string AssemblyPath { get; init; }

    public required string AssemblyName { get; init; }

    public required SingleFileBuildManifest Manifest { get; init; }
}

/// <summary>
/// The build cache under one store: the layouts under <c>cache/extensions/&lt;id&gt;/builds/</c>,
/// the <c>builds.json</c> pointer, and the retention rule. A build is written to a temporary
/// directory and moved into place, so a reader never sees half an assembly; the pointer is written
/// the same way. A successful compile keeps <see cref="SingleFileExtensionPaths.KeptBuilds"/>
/// builds and deletes the rest — the edit, fail, fix loop never grows the cache past two
/// assemblies. Reading is a directory existence check plus a small manifest parse: no Roslyn, no
/// NuGet, nothing on the startup budget beyond a stat.
/// </summary>
public sealed class SingleFileBuildCache
{
    private readonly string _storePath;

    public SingleFileBuildCache(string storePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);
        _storePath = storePath;
    }

    /// <summary>
    /// The build for <paramref name="key"/>, or null when there is none. A directory without its
    /// assembly or without a readable manifest is not a build: a truncated write is invisible
    /// rather than loadable, and the next compile replaces the key's directory.
    /// </summary>
    public SingleFileBuild? Find(string id, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!SingleFileBuildKey.IsKey(key))
        {
            return null;
        }

        string buildPath = SingleFileExtensionPaths.BuildPath(_storePath, id, key);
        string assemblyPath = SingleFileExtensionPaths.AssemblyPath(_storePath, id, key);
        if (!File.Exists(assemblyPath))
        {
            return null;
        }

        SingleFileBuildManifest? manifest = ReadManifest(
            SingleFileExtensionPaths.BuildManifestPath(_storePath, id, key)
        );
        return manifest is null
            ? null
            : new SingleFileBuild
            {
                Key = key,
                ExtensionId = id,
                BuildPath = buildPath,
                AssemblyPath = assemblyPath,
                AssemblyName = SingleFileExtensionPaths.AssemblyFileName(id),
                Manifest = manifest,
            };
    }

    /// <summary>
    /// The build <c>builds.json</c> points at, or null when it points at nothing usable (a build
    /// deleted by hand, an index that was never written).
    /// </summary>
    public SingleFileBuild? Current(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        BuildsIndex index = ReadIndex(SingleFileExtensionPaths.BuildsIndexPath(_storePath, id));
        return index.Current is { } key ? Find(id, key) : null;
    }

    /// <summary>The keys the index records, newest first.</summary>
    public IReadOnlyList<string> Keys(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return ReadIndex(SingleFileExtensionPaths.BuildsIndexPath(_storePath, id)).Keys;
    }

    /// <summary>
    /// Writes one compiled assembly as a new build, points the entry at it and keeps the newest
    /// <see cref="SingleFileExtensionPaths.KeptBuilds"/> builds. The order is deliberate: the build
    /// is complete on disk before the pointer names it, and the pointer names it before anything is
    /// deleted, so an interrupted write leaves an unreferenced directory rather than a broken entry.
    /// </summary>
    public SingleFileBuild Save(
        string id,
        string key,
        ReadOnlySpan<byte> assembly,
        SingleFileBuildManifest manifest
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(manifest);
        if (!SingleFileBuildKey.IsKey(key))
        {
            throw new ArgumentException($"'{key}' is not a build key.", nameof(key));
        }

        string entry = SingleFileExtensionPaths.EntryPath(_storePath, id);
        string builds = SingleFileExtensionPaths.BuildsPath(_storePath, id);
        string buildPath = SingleFileExtensionPaths.BuildPath(_storePath, id, key);
        Directory.CreateDirectory(builds);

        string temporary = Path.Combine(
            builds,
            "." + key + "." + Guid.NewGuid().ToString("N") + ".tmp"
        );
        Directory.CreateDirectory(temporary);
        try
        {
            File.WriteAllBytes(
                Path.Combine(temporary, SingleFileExtensionPaths.AssemblyFileName(id)),
                assembly
            );
            File.WriteAllBytes(
                Path.Combine(temporary, SingleFileBuildManifest.FileName),
                SingleFileBuildJson.ManifestBytes(manifest)
            );

            // A stale directory for this key (a half write, a hand edit) is replaced, not merged.
            if (Directory.Exists(buildPath))
            {
                Directory.Delete(buildPath, recursive: true);
            }

            Directory.Move(temporary, buildPath);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        List<string> kept = Keep(id, key);
        WriteIndex(SingleFileExtensionPaths.BuildsIndexPath(_storePath, id), key, kept);

        return Find(id, key)
            ?? throw new IOException(
                FormattableString.Invariant(
                    $"the build for '{id}' could not be read back from '{entry}'."
                )
            );
    }

    /// <summary>
    /// Points the entry at a build that is already on disk — the cache-hit path, which creates
    /// nothing and therefore deletes nothing.
    /// </summary>
    public void MarkCurrent(SingleFileBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);

        List<string> keys = [build.Key];
        keys.AddRange(
            Keys(build.ExtensionId)
                .Where(key => !string.Equals(key, build.Key, StringComparison.Ordinal))
        );
        WriteIndex(
            SingleFileExtensionPaths.BuildsIndexPath(_storePath, build.ExtensionId),
            build.Key,
            keys
        );
    }

    /// <summary>
    /// The directory names under <c>builds/</c> that look like keys, whether or not the index
    /// mentions them: the input retention starts from, so a build the index never heard of (an
    /// interrupted write, a hand copy) is still cleaned up.
    /// </summary>
    private IReadOnlyList<string> StoredKeys(string id)
    {
        string builds = SingleFileExtensionPaths.BuildsPath(_storePath, id);
        if (!Directory.Exists(builds))
        {
            return [];
        }

        return
        [
            .. Directory
                .GetDirectories(builds)
                .Select(directory => Path.GetFileName(directory))
                .Where(SingleFileBuildKey.IsKey)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// The retention rule, applied after a successful write: the new key plus the newest existing
    /// build stay, everything else under <c>builds/</c> is deleted. Returns the keys that survived,
    /// newest first — the list the index records.
    /// </summary>
    private List<string> Keep(string id, string key)
    {
        HashSet<string> existing = new(StoredKeys(id), StringComparer.Ordinal);
        existing.Remove(key);

        List<string> previous = [.. Keys(id).Where(existing.Contains)];
        // Anything the index never heard of (an interrupted write, a hand copy) is older than the
        // builds it does name.
        previous.AddRange(
            existing.Except(previous, StringComparer.Ordinal).Order(StringComparer.Ordinal)
        );

        List<string> kept = [key, .. previous.Take(SingleFileExtensionPaths.KeptBuilds - 1)];

        string builds = SingleFileExtensionPaths.BuildsPath(_storePath, id);
        foreach (string directory in Directory.GetDirectories(builds))
        {
            string name = Path.GetFileName(directory);
            if (!kept.Contains(name, StringComparer.Ordinal))
            {
                TryDelete(directory);
            }
        }

        return kept;
    }

    private void WriteIndex(string path, string? current, IReadOnlyList<string> keys)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);

        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(
            temporary,
            SingleFileBuildJson.IndexBytes(new BuildsIndex(current, keys))
        );
        File.Move(temporary, path, overwrite: true);
    }

    private static SingleFileBuildManifest? ReadManifest(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return SingleFileBuildJson.ReadManifest(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static BuildsIndex ReadIndex(string path)
    {
        try
        {
            return File.Exists(path)
                ? SingleFileBuildJson.ReadIndex(File.ReadAllText(path))
                : BuildsIndex.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return BuildsIndex.Empty;
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A directory that cannot be deleted is left for the next build; it is unreachable
            // from the pointer either way, so the failure is a cache-size problem, not a broken
            // extension.
        }
    }
}

/// <summary>The pointer file: the current build's key and the builds the entry still keeps.</summary>
internal sealed record BuildsIndex(string? Current, IReadOnlyList<string> Keys)
{
    internal static BuildsIndex Empty { get; } = new(null, []);
}

/// <summary>
/// Reads and writes the two small documents of the build cache. Both writers are canonical — keys
/// are ordered by the caller and the manifest's assets are sorted here — so the same build always
/// serializes to the same bytes.
/// </summary>
internal static class SingleFileBuildJson
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    internal static byte[] ManifestBytes(SingleFileBuildManifest manifest) =>
        Encoding.UTF8.GetBytes(Document(manifest).ToJsonString(Indented) + "\n");

    internal static byte[] IndexBytes(BuildsIndex index)
    {
        var document = new JsonObject();
        if (index.Current is { } current)
        {
            document["current"] = current;
        }

        var builds = new JsonArray();
        foreach (string key in index.Keys)
        {
            builds.Add(key);
        }

        document["builds"] = builds;
        return Encoding.UTF8.GetBytes(document.ToJsonString(Indented) + "\n");
    }

    internal static SingleFileBuildManifest? ReadManifest(string json)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(json);
            if (node is not JsonObject document)
            {
                return null;
            }

            int version = document["version"]?.GetValue<int>() ?? 0;
            if (version != SingleFileBuildManifest.CurrentVersion)
            {
                return null;
            }

            string? Key(string name) => document[name]?.GetValue<string>();

            if (
                Key("key") is not { } key
                || Key("assembly") is not { } assembly
                || Key("extensionId") is not { } id
                || Key("extensionVersion") is not { } extensionVersion
                || Key("apiVersion") is not { } apiVersion
                || Key("tfm") is not { } tfm
                || Key("rid") is not { } rid
                || Key("sourceHash") is not { } sourceHash
                || Key("lockHash") is not { } lockHash
            )
            {
                return null;
            }

            List<SingleFileBuildPackage> packages = [];
            foreach (JsonNode? entry in document["packages"] as JsonArray ?? [])
            {
                if (
                    entry is not JsonObject package
                    || package["id"]?.GetValue<string>() is not { } packageId
                    || package["version"]?.GetValue<string>() is not { } packageVersion
                )
                {
                    return null;
                }

                packages.Add(
                    new SingleFileBuildPackage
                    {
                        Id = packageId,
                        Version = packageVersion,
                        Runtime = Strings(package["runtime"]),
                        Native = Strings(package["native"]),
                    }
                );
            }

            return new SingleFileBuildManifest
            {
                Key = key,
                Assembly = assembly,
                ExtensionId = id,
                ExtensionVersion = extensionVersion,
                ApiVersion = apiVersion,
                Tfm = tfm,
                Rid = rid,
                SourceHash = sourceHash,
                LockHash = lockHash,
                Packages = packages,
            };
        }
        catch (Exception exception)
            when (exception
                    is JsonException
                        or InvalidOperationException
                        or FormatException
                        or NotSupportedException
            )
        {
            return null;
        }
    }

    internal static BuildsIndex ReadIndex(string json)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(json);
            if (node is not JsonObject document)
            {
                return BuildsIndex.Empty;
            }

            string? current = document["current"]?.GetValue<string>();
            return new BuildsIndex(
                SingleFileBuildKey.IsKey(current) ? current : null,
                [.. Strings(document["builds"]).Where(SingleFileBuildKey.IsKey)]
            );
        }
        catch (Exception exception)
            when (exception
                    is JsonException
                        or InvalidOperationException
                        or FormatException
                        or NotSupportedException
            )
        {
            return BuildsIndex.Empty;
        }
    }

    private static JsonObject Document(SingleFileBuildManifest manifest)
    {
        var packages = new JsonArray();
        foreach (
            SingleFileBuildPackage package in manifest.Packages.OrderBy(
                entry => entry.Id,
                StringComparer.Ordinal
            )
        )
        {
            packages.Add(
                new JsonObject
                {
                    ["id"] = package.Id,
                    ["version"] = package.Version,
                    ["runtime"] = Array(package.Runtime),
                    ["native"] = Array(package.Native),
                }
            );
        }

        return new JsonObject
        {
            ["version"] = manifest.Version,
            ["key"] = manifest.Key,
            ["assembly"] = manifest.Assembly,
            ["extensionId"] = manifest.ExtensionId,
            ["extensionVersion"] = manifest.ExtensionVersion,
            ["apiVersion"] = manifest.ApiVersion,
            ["tfm"] = manifest.Tfm,
            ["rid"] = manifest.Rid,
            ["sourceHash"] = manifest.SourceHash,
            ["lockHash"] = manifest.LockHash,
            ["packages"] = packages,
        };
    }

    private static JsonArray Array(IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (string value in values.Order(StringComparer.Ordinal))
        {
            array.Add(value);
        }

        return array;
    }

    private static List<string> Strings(JsonNode? node) =>
        node is JsonArray array
            ? [.. array.Select(entry => entry?.GetValue<string>()).OfType<string>()]
            : [];
}

/// <summary>
/// Removes what no extension points at: a cache entry is an orphan when no discovered single-file
/// extension declares its id — which is also how removing the extension deletes its builds, since
/// deleting the <c>.cs</c> file is what makes the entry an orphan. The pruner never touches the
/// shared NuGet packages folder (it is content-addressed and NuGet's, not ours) and never touches an
/// entry whose id is still declared, however old it is.
/// </summary>
public static class SingleFileCachePruner
{
    /// <summary>
    /// Deletes every entry under <paramref name="storePath"/> whose name is not in
    /// <paramref name="liveIds"/>; returns the ids it removed, in ordinal order. An entry that
    /// cannot be deleted is reported, not thrown: one locked directory must not stop the sweep.
    /// </summary>
    public static IReadOnlyList<string> Prune(string storePath, IReadOnlyCollection<string> liveIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);
        ArgumentNullException.ThrowIfNull(liveIds);

        string root = SingleFileExtensionPaths.CacheRoot(storePath);
        if (!Directory.Exists(root))
        {
            return [];
        }

        HashSet<string> live = new(liveIds, StringComparer.Ordinal);
        List<string> removed = [];
        foreach (string entry in Directory.GetDirectories(root))
        {
            string id = Path.GetFileName(entry);
            if (live.Contains(id))
            {
                continue;
            }

            try
            {
                Directory.Delete(entry, recursive: true);
                removed.Add(id);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                // Left in place: the next prune sees it again.
            }
        }

        removed.Sort(StringComparer.Ordinal);
        return removed;
    }
}
