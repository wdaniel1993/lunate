using Lunate.Extensibility.Abstractions;
using Lunate.Roslyn;

namespace Lunate.Coding;

/// <summary>
/// The registry's file-change bus subscription: the Roslyn backend is the bus's first core
/// subscriber. One session-lifetime subscription with a null pattern (the handler filters
/// explicitly): a <c>.cs</c> event marks the file on the backend of the event's workspace id; a
/// project, solution or build-configuration file marks that workspace for a reload. Marks never
/// create a backend — a root with no backend ignores the event, because its first load reads disk
/// anyway.
/// </summary>
internal sealed partial class WorkspaceServices
{
    private static readonly string[] ReloadFileNames =
    [
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "global.json",
    ];

    private IDisposable? _fileChanges;

    /// <summary>
    /// Subscribes to the session's file-change bus; the subscription lives until the registry is
    /// disposed (the bus's own disposal drops it regardless). One subscription per registry: a
    /// second call is ignored.
    /// </summary>
    internal void Connect(IFileChangeBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);

        lock (_gate)
        {
            if (_disposed || _fileChanges is not null)
            {
                return;
            }
        }

        IDisposable subscription = bus.Subscribe(new FileChangeHandler(this));
        bool kept;
        lock (_gate)
        {
            kept = !_disposed && _fileChanges is null;
            if (kept)
            {
                _fileChanges = subscription;
            }
        }

        if (!kept)
        {
            // The registry disposed (or a concurrent Connect won) while subscribing.
            subscription.Dispose();
        }
    }

    /// <summary>
    /// Marks one source file of the event's workspace: a non-creating lookup under the gate, so a
    /// root without a backend creates nothing; the backend mark itself never blocks.
    /// </summary>
    internal void MarkChanged(string workspaceId, string path) =>
        ExistingBackend(workspaceId)?.NotifyFileChanged(path);

    /// <summary>
    /// Marks the event's workspace for a reload (a project, solution or build-configuration
    /// change): the next call on that root disposes the loaded solution and reloads from disk.
    /// </summary>
    internal void MarkProjectChanged(string workspaceId) =>
        ExistingBackend(workspaceId)?.NotifyProjectChanged();

    /// <summary>The backend of an existing, live entry; null for an absent or disposed root.</summary>
    private ICSharpBackend? ExistingBackend(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return null;
        }

        lock (_gate)
        {
            return
                !_disposed && _backends.TryGetValue(workspaceId, out Entry? entry) && !entry.Evicted
                ? entry.Backend
                : null;
        }
    }

    /// <summary>
    /// The bus handler: filters by path and returns immediately. The mark path does no I/O and
    /// never waits on the backend gate, so a mark cannot hit the bus handler timeout even while a
    /// cold load or a long compile holds the gate.
    /// </summary>
    private sealed class FileChangeHandler(WorkspaceServices services) : IFileChangedHandler
    {
        public int Priority => 0;

        public ValueTask OnFileChangedAsync(
            FileChangedPayload payload,
            CancellationToken cancellationToken
        )
        {
            if (payload.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                services.MarkChanged(payload.WorkspaceId, payload.Path);
            }
            else if (IsReloadTrigger(payload.Path))
            {
                services.MarkProjectChanged(payload.WorkspaceId);
            }

            return ValueTask.CompletedTask;
        }

        private static bool IsReloadTrigger(string path)
        {
            if (
                path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }

            string name = Path.GetFileName(path);
            return ReloadFileNames.Contains(name, StringComparer.OrdinalIgnoreCase);
        }
    }
}
