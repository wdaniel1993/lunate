namespace Lunate.Extensibility;

/// <summary>
/// Where a single-file extension's Lunate-owned files live: one cache entry per declared id under
/// <c>~/.lunate/cache/extensions/&lt;id&gt;/</c>, holding the lock and (from the compile phase) the
/// build directories. The extension's own source stays wherever the user put it.
/// </summary>
public static class SingleFileExtensionPaths
{
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
}
