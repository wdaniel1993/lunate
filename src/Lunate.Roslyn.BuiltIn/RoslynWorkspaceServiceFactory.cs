using Lunate.Extensibility.Abstractions;
using Lunate.Roslyn;

namespace Lunate.Roslyn.BuiltIn;

internal sealed class RoslynWorkspaceServiceFactory(
    Func<string, ICSharpBackend>? createBackend = null
) : IWorkspaceServiceFactory<RoslynWorkspaceService>, IFileChangedHandler
{
    private static readonly string[] ReloadFileNames =
    [
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "global.json",
    ];

    private readonly object _gate = new();
    private readonly Dictionary<string, HashSet<RoslynWorkspaceService>> _live = new(
        RootComparison
    );
    private readonly Func<string, ICSharpBackend> _createBackend = createBackend ?? CreateBackend;

    public int Priority => 0;

    public RoslynWorkspaceService Create(string canonicalWorktreeRoot)
    {
        var service = new RoslynWorkspaceService(
            _createBackend(canonicalWorktreeRoot),
            value => Remove(canonicalWorktreeRoot, value)
        );
        lock (_gate)
        {
            if (
                !_live.TryGetValue(
                    canonicalWorktreeRoot,
                    out HashSet<RoslynWorkspaceService>? values
                )
            )
            {
                values = [];
                _live.Add(canonicalWorktreeRoot, values);
            }

            values.Add(service);
        }

        return service;
    }

    public ValueTask OnFileChangedAsync(
        FileChangedPayload payload,
        CancellationToken cancellationToken
    )
    {
        RoslynWorkspaceService[] services;
        lock (_gate)
        {
            services = _live.TryGetValue(
                payload.WorkspaceId,
                out HashSet<RoslynWorkspaceService>? values
            )
                ? [.. values]
                : [];
        }

        foreach (RoslynWorkspaceService service in services)
        {
            if (payload.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                service.MarkFileChanged(payload.Path);
            }
            else if (IsReloadTrigger(payload.Path))
            {
                service.MarkProjectChanged();
            }
        }

        return ValueTask.CompletedTask;
    }

    private void Remove(string root, RoslynWorkspaceService service)
    {
        lock (_gate)
        {
            if (
                _live.TryGetValue(root, out HashSet<RoslynWorkspaceService>? values)
                && values.Remove(service)
                && values.Count == 0
            )
            {
                _live.Remove(root);
            }
        }
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

        return ReloadFileNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
    }

    private static StringComparer RootComparison =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    private static ICSharpBackend CreateBackend(string root) => new RoslynBackend(root);
}

internal sealed class RoslynWorkspaceService(
    ICSharpBackend backend,
    Action<RoslynWorkspaceService> onDispose
) : IDisposable
{
    private readonly object _gate = new();
    private bool _disposed;

    public ICSharpBackend Backend { get; } = backend;

    public void MarkFileChanged(string path)
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                Backend.NotifyFileChanged(path);
            }
        }
    }

    public void MarkProjectChanged()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                Backend.NotifyProjectChanged();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        (Backend as IDisposable)?.Dispose();
        onDispose(this);
    }
}
