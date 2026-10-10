namespace Lunate.Coding;

/// <summary>
/// The workspace file index for `@path` completion: built lazily on a background task (never at
/// startup), bounded to <see cref="EntryCap"/> sorted entries, and queried by ordinal prefix.
/// Entries are pushed through the <see cref="IWorkspaceFiles"/> seam, filtered root-down by the
/// `.gitignore` chain (deeper files win; an excluded directory prunes its subtree), always
/// without `.git`, then sorted and capped — sorting before capping makes the retained set
/// deterministic regardless of walk order.
/// </summary>
internal sealed class FileIndex
{
    /// <summary>The maximum number of entries the index keeps; beyond it the flag is set.</summary>
    internal const int EntryCap = 200_000;

    private readonly IWorkspaceFiles _files;
    private readonly object _gate = new();
    private Task? _build;
    private volatile IReadOnlyList<string>? _entries;
    private volatile bool _truncated;

    public FileIndex(IWorkspaceFiles files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files;
    }

    /// <summary>Whether the build has published its result.</summary>
    public bool IsReady => _entries is not null;

    /// <summary>Whether the workspace exceeded <see cref="EntryCap"/> and entries were dropped.</summary>
    public bool IsTruncated => _truncated;

    /// <summary>Starts the background build once; repeated calls are no-ops.</summary>
    public void EnsureStarted()
    {
        lock (_gate)
        {
            _build ??= Task.Run(Build);
        }
    }

    /// <summary>
    /// The sorted entries starting with <paramref name="prefix"/> (ordinal), at most
    /// <paramref name="max"/> of them; <paramref name="max"/> of zero or less means no cap.
    /// Before the build publishes, the result is empty.
    /// </summary>
    public IReadOnlyList<string> Match(string prefix, int max)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (_entries is not { } entries)
        {
            return [];
        }

        List<string> matches = [];
        for (int index = LowerBound(entries, prefix); index < entries.Count; index++)
        {
            if (!entries[index].StartsWith(prefix, StringComparison.Ordinal))
            {
                break;
            }

            if (max > 0 && matches.Count >= max)
            {
                break;
            }

            matches.Add(entries[index]);
        }

        return matches;
    }

    private static int LowerBound(IReadOnlyList<string> entries, string prefix)
    {
        var low = 0;
        var high = entries.Count;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (string.CompareOrdinal(entries[middle], prefix) < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private void Build()
    {
        IReadOnlyList<string> entries;
        var truncated = false;
        try
        {
            (entries, truncated) = Collect();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            entries = [];
        }

        _truncated = truncated;
        _entries = entries;
    }

    private (IReadOnlyList<string> Entries, bool Truncated) Collect()
    {
        IReadOnlyList<WorkspaceEntry> all = _files.Enumerate();
        Dictionary<string, GitIgnore> scopes = LoadScopes(all);
        HashSet<string> excludedDirectories = new(StringComparer.Ordinal);
        List<string> entries = [];
        foreach (
            WorkspaceEntry entry in all.Where(entry => entry.IsDirectory)
                .OrderBy(entry => entry.Path, StringComparer.Ordinal)
        )
        {
            if (IsGit(entry.Path))
            {
                continue;
            }

            if (IsExcluded(entry.Path, isDirectory: true, scopes, excludedDirectories))
            {
                excludedDirectories.Add(entry.Path);
                continue;
            }

            entries.Add(entry.Path + "/");
        }

        foreach (WorkspaceEntry entry in all)
        {
            if (entry.IsDirectory || IsGit(entry.Path))
            {
                continue;
            }

            if (!IsExcluded(entry.Path, isDirectory: false, scopes, excludedDirectories))
            {
                entries.Add(entry.Path);
            }
        }

        entries.Sort(StringComparer.Ordinal);
        var truncated = entries.Count > EntryCap;
        if (truncated)
        {
            entries.RemoveRange(EntryCap, entries.Count - EntryCap);
        }

        return (entries, truncated);
    }

    private Dictionary<string, GitIgnore> LoadScopes(IReadOnlyList<WorkspaceEntry> all)
    {
        Dictionary<string, GitIgnore> scopes = new(StringComparer.Ordinal);
        foreach (WorkspaceEntry entry in all)
        {
            if (entry.IsDirectory || !IsGitIgnore(entry.Path))
            {
                continue;
            }

            if (_files.ReadAllText(entry.Path) is not { } content)
            {
                continue;
            }

            scopes[ParentDirectory(entry.Path)] = GitIgnore.Parse(content);
        }

        return scopes;
    }

    private static bool IsExcluded(
        string path,
        bool isDirectory,
        Dictionary<string, GitIgnore> scopes,
        HashSet<string> excludedDirectories
    )
    {
        for (
            string? parent = ParentDirectory(path);
            parent.Length > 0;
            parent = ParentDirectory(parent)
        )
        {
            if (excludedDirectories.Contains(parent))
            {
                return true;
            }
        }

        bool? verdict = null;
        foreach (string scope in AncestorChain(ParentDirectory(path)))
        {
            if (!scopes.TryGetValue(scope, out GitIgnore? ignore))
            {
                continue;
            }

            string relative = scope.Length == 0 ? path : path[(scope.Length + 1)..];
            if (ignore.Match(relative, isDirectory) is { } matched)
            {
                verdict = matched;
            }
        }

        return verdict ?? false;
    }

    private static IEnumerable<string> AncestorChain(string directory)
    {
        yield return string.Empty;
        var index = 0;
        while (index < directory.Length)
        {
            int slash = directory.IndexOf('/', index);
            if (slash < 0)
            {
                yield return directory;
                yield break;
            }

            yield return directory[..slash];
            index = slash + 1;
        }
    }

    private static string ParentDirectory(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    private static bool IsGit(string path) =>
        string.Equals(path, ".git", StringComparison.Ordinal)
        || path.StartsWith(".git/", StringComparison.Ordinal);

    private static bool IsGitIgnore(string path) =>
        string.Equals(path, ".gitignore", StringComparison.Ordinal)
        || path.EndsWith("/.gitignore", StringComparison.Ordinal);
}
