namespace WorktreeTasksExtension;

/// <summary>
/// The extension's live state: the worktree paths with a spawn in flight. A clean refuses while a
/// path is active, so a run is never deleted under itself.
/// </summary>
internal sealed class WorktreeTasksState
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _active = new(PathComparer);

    private static StringComparer PathComparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public void MarkActive(string path)
    {
        string key = Path.GetFullPath(path);
        lock (_gate)
        {
            _active[key] = _active.TryGetValue(key, out int count) ? count + 1 : 1;
        }
    }

    public void UnmarkActive(string path)
    {
        string key = Path.GetFullPath(path);
        lock (_gate)
        {
            if (!_active.TryGetValue(key, out int count))
            {
                return;
            }

            if (count <= 1)
            {
                _active.Remove(key);
            }
            else
            {
                _active[key] = count - 1;
            }
        }
    }

    public bool IsActive(string path)
    {
        string key = Path.GetFullPath(path);
        lock (_gate)
        {
            return _active.ContainsKey(key);
        }
    }
}
