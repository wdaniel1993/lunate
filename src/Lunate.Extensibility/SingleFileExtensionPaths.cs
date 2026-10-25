namespace Lunate.Extensibility;

/// <summary>
/// Where a single-file extension's Lunate-owned files live: one cache entry per declared id under
/// <c>~/.lunate/cache/extensions/&lt;id&gt;/</c>, holding the lock and the builds. The compiled
/// assembly never sits at the entry root: it lives in <c>builds/&lt;key&gt;/</c> beside a manifest
/// that records what it was built from, and <c>builds.json</c> points at the current one. The
/// extension's own source stays wherever the user put it.
/// </summary>
public static class SingleFileExtensionPaths
{
    /// <summary>Only two builds survive a successful compile; the pointer names the newest.</summary>
    public const int KeptBuilds = 2;

    /// <summary>The cache root below the store, shared with packaged-extension builds.</summary>
    public static string CacheRoot(string storePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);
        return Path.Combine(storePath, "cache", "extensions");
    }

    /// <summary>The directory that holds one extension's lock and builds.</summary>
    public static string EntryPath(string storePath, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Path.Combine(CacheRoot(storePath), id);
    }

    /// <summary>
    /// The lock file of one extension. It is keyed by id only — not by build key — because the
    /// build key includes the lock hash: the lock is the input, the build is the output.
    /// </summary>
    public static string LockPath(string storePath, string id) =>
        Path.Combine(EntryPath(storePath, id), ExtensionLock.FileName);

    /// <summary>The directory that holds every build of one extension, oldest kept last.</summary>
    public static string BuildsPath(string storePath, string id) =>
        Path.Combine(EntryPath(storePath, id), "builds");

    /// <summary>One build's directory, named by its key.</summary>
    public static string BuildPath(string storePath, string id, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Path.Combine(BuildsPath(storePath, id), key);
    }

    /// <summary>The entry's pointer to the current build: <c>{ current, builds }</c>.</summary>
    public static string BuildsIndexPath(string storePath, string id) =>
        Path.Combine(EntryPath(storePath, id), "builds.json");

    /// <summary>The compiled assembly of one build; the file name is the extension id.</summary>
    public static string AssemblyPath(string storePath, string id, string key) =>
        Path.Combine(BuildPath(storePath, id, key), AssemblyFileName(id));

    /// <summary>The manifest a build directory carries beside its assembly.</summary>
    public static string BuildManifestPath(string storePath, string id, string key) =>
        Path.Combine(BuildPath(storePath, id, key), "build.json");

    /// <summary>
    /// The compiled assembly's file name. The declared id is already restricted to
    /// <c>[a-z0-9-]+</c>, so it is a legal assembly simple name as written.
    /// </summary>
    public static string AssemblyFileName(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return id + ".dll";
    }
}
