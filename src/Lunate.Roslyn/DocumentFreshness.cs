namespace Lunate.Roslyn;

/// <summary>
/// Per-document staleness detection: a (last write, length) snapshot per path. Pure file-system
/// state, no Roslyn types. Dirty marks from <see cref="ICSharpBackend.NotifyFileChanged"/> are
/// tracked by the backend because they can arrive before a workspace exists.
/// </summary>
internal sealed class DocumentFreshness
{
    private readonly Dictionary<string, FileStamp> _stamps = new(PathIdentity.Comparer);

    /// <summary>Snapshots the current state of <paramref name="path"/> (a missing file is untracked).</summary>
    public void Track(string path)
    {
        if (ReadStamp(path) is { } stamp)
        {
            _stamps[path] = stamp;
        }
        else
        {
            _stamps.Remove(path);
        }
    }

    /// <summary>
    /// The subset of <paramref name="paths"/> that changed (content stamp or deletion) since the
    /// previous scan; updates the snapshot to the current state.
    /// </summary>
    public IReadOnlyList<string> TakeChanged(IReadOnlyCollection<string> paths)
    {
        List<string> changed = [];
        foreach (var path in paths)
        {
            var current = ReadStamp(path);
            var previous = _stamps.TryGetValue(path, out var stamp) ? stamp : (FileStamp?)null;

            if (current != previous)
            {
                changed.Add(path);
            }

            if (current is { } updated)
            {
                _stamps[path] = updated;
            }
            else
            {
                _stamps.Remove(path);
            }
        }

        return changed;
    }

    private static FileStamp? ReadStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.LastWriteTimeUtc, info.Length) : null;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private readonly record struct FileStamp(DateTime LastWriteUtc, long Length);
}
