namespace Lunate.Agent;

/// <summary>
/// Per-path serialization via one <see cref="SemaphoreSlim"/> per canonical path. Gates are kept
/// for the process lifetime: dropping a free gate is not trivially safe (a waiter can already hold
/// a reference to the instance being removed, so a later caller would create a second gate for the
/// same path), and the dictionary stays bounded by the paths a process mutates.
/// </summary>
public sealed class FileMutationQueue : IFileMutationQueue
{
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

        SemaphoreSlim gate = GateFor(Path.GetFullPath(path));
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
