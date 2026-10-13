namespace Lunate.Agent;

/// <summary>
/// Per-path serialization via one <see cref="SemaphoreSlim"/> per canonical path. Paths are keyed
/// by their full spelling with symbolic links resolved, so two spellings of one file share one
/// gate; the key comparison follows the platform's default filesystem (case-insensitive on Windows
/// and macOS, ordinal on Linux). Gates are kept for the process lifetime: dropping a free gate is
/// not trivially safe (a waiter can already hold a reference to the instance being removed, so a
/// later caller would create a second gate for the same path), and the dictionary stays bounded by
/// the paths a process mutates.
/// </summary>
public sealed class FileMutationQueue : IFileMutationQueue
{
    private const int MaxLinkHops = 40;

    private readonly Dictionary<string, SemaphoreSlim> _gates = new(
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase
    );

    /// <summary>The process-wide default queue; file-mutating tools share it unless given another.</summary>
    public static FileMutationQueue Shared { get; } = new();

    public async Task<T> RunAsync<T>(
        string path,
        Func<CancellationToken, Task<T>> mutation,
        CancellationToken ct
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(mutation);

        SemaphoreSlim gate = GateFor(NormalizeKey(path));
        await gate.WaitAsync(ct);
        try
        {
            return await mutation(ct);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// The lock key of a path: the full path with every symbolic link of an existing component
    /// resolved (a not-yet-existing trailing component keeps its name under its resolved parent),
    /// so two spellings of one file cannot hold two gates.
    /// </summary>
    internal static string NormalizeKey(string path) => ResolveLinks(Path.GetFullPath(path), 0);

    private static string ResolveLinks(string full, int depth)
    {
        if (depth > MaxLinkHops)
        {
            return full;
        }

        string? root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
        {
            return full;
        }

        string current = root;
        foreach (
            string segment in full[root.Length..]
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries
                )
        )
        {
            string next = Path.Combine(current, segment);
            string? target = LinkTargetOf(next);
            current = target is null ? next : ResolveLinks(Path.GetFullPath(target), depth + 1);
        }

        return current;
    }

    /// <summary>
    /// The link's raw target resolved against the link's own directory, or null when the component
    /// is not a link. Works for dangling links too — their target does not have to exist.
    /// </summary>
    private static string? LinkTargetOf(string path)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path)
                ? new DirectoryInfo(path)
                : new FileInfo(path);
            var target = info.LinkTarget;
            if (target is null)
            {
                return null;
            }

            return Path.IsPathRooted(target)
                ? target
                : Path.Combine(Path.GetDirectoryName(path)!, target);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private SemaphoreSlim GateFor(string canonicalPath)
    {
        lock (_gates)
        {
            if (!_gates.TryGetValue(canonicalPath, out SemaphoreSlim? gate))
            {
                gate = new SemaphoreSlim(1, 1);
                _gates.Add(canonicalPath, gate);
            }

            return gate;
        }
    }
}
