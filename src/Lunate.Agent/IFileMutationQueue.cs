namespace Lunate.Agent;

/// <summary>
/// Serializes read-modify-write mutations per canonical path, so concurrent edits to one file
/// apply one after another instead of losing updates.
/// </summary>
public interface IFileMutationQueue
{
    /// <summary>Runs <paramref name="mutation"/> exclusively for <paramref name="path"/>.</summary>
    Task<T> RunAsync<T>(
        string path,
        Func<CancellationToken, Task<T>> mutation,
        CancellationToken ct
    );
}
