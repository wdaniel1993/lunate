namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// A session-scoped background service: an LSP server, watcher or connection. The host starts it
/// with <c>SessionStarted</c> semantics and stops it idempotently on <c>SessionEnding</c>. A
/// failure is reported and contained: it never crashes the run.
/// </summary>
public interface IBackgroundService
{
    ValueTask StartAsync(CancellationToken cancellationToken);

    ValueTask StopAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Read-only handle to the core file change bus. A subscription may carry a pattern: <c>*</c>
/// matches any characters and <c>?</c> exactly one; patterns match the whole canonical absolute
/// path and compare case-sensitively on Linux, case-insensitively elsewhere. A null or empty
/// pattern matches every change.
/// </summary>
public interface IFileChangeBus
{
    /// <summary>Subscribes for the extension's lifetime; disposal ends the subscription.</summary>
    IDisposable Subscribe(IFileChangedHandler handler, string? pathPattern = null);
}

/// <summary>
/// Read-only handle to the core mutation queue: schedules a read-modify-write on a canonical
/// absolute path so concurrent mutations of one file apply one after another.
/// </summary>
public interface IMutationQueue
{
    Task<T> RunAsync<T>(
        string path,
        Func<CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken
    );
}

/// <summary>Core workspace identity exposed to extensions.</summary>
public sealed record WorkspaceInfo(string WorktreeRoot, string? RepoRoot, string? GitCommonDir);
