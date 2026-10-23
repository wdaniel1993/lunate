using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

internal sealed class ExtensionContext : IExtensionContext
{
    private readonly Action<string, IHookHandler>? _register;
    private readonly Action<string, string, IBackgroundService>? _registerService;
    private readonly Action<string, IFileChangedHandler, string?>? _subscribeFileChanged;
    private readonly Action<string, IToolResultRenderer>? _registerToolResultRenderer;
    private readonly Action<string, IStatusWidget>? _registerStatusWidget;
    private readonly Action<string, McpServerRegistration>? _registerMcpServer;
    private readonly Action<string, ExtensionToolRegistration>? _registerTool;
    private readonly Action<string, ExtensionModelProvider>? _registerModelProvider;
    private readonly Action<string, ExtensionModelMiddleware>? _registerModelMiddleware;
    private readonly Action<string, VirtualModelRegistration>? _registerVirtualModel;
    private readonly Action<string, ISkillSource>? _registerSkillSource;
    private readonly Action<string, ICommandSource>? _registerCommandSource;
    private readonly Func<string, IBackgroundService?>? _findService;
    private readonly Func<string, object?>? _findCoreService;
    private readonly IExtensionSettings _settings;
    private readonly IExtensionSecrets _secrets;
    private volatile bool _disposed;

    public ExtensionContext(
        string id,
        IExtensionSettings settings,
        IExtensionSecrets secrets,
        IExtensionLog log,
        Action<string, IHookHandler>? register = null,
        Action<string, string, IBackgroundService>? registerService = null,
        Action<string, IFileChangedHandler, string?>? subscribeFileChanged = null,
        Func<string, IBackgroundService?>? findService = null,
        Func<string, object?>? findCoreService = null,
        Action<string, IToolResultRenderer>? registerToolResultRenderer = null,
        Action<string, IStatusWidget>? registerStatusWidget = null,
        Action<string, McpServerRegistration>? registerMcpServer = null,
        Action<string, ExtensionToolRegistration>? registerTool = null,
        Action<string, ExtensionModelProvider>? registerModelProvider = null,
        Action<string, ExtensionModelMiddleware>? registerModelMiddleware = null,
        Action<string, VirtualModelRegistration>? registerVirtualModel = null,
        Action<string, ISkillSource>? registerSkillSource = null,
        Action<string, ICommandSource>? registerCommandSource = null
    )
    {
        Id = id;
        _settings = settings;
        _secrets = secrets;
        Log = log;
        _register = register;
        _registerService = registerService;
        _subscribeFileChanged = subscribeFileChanged;
        _registerToolResultRenderer = registerToolResultRenderer;
        _registerStatusWidget = registerStatusWidget;
        _registerMcpServer = registerMcpServer;
        _registerTool = registerTool;
        _registerModelProvider = registerModelProvider;
        _registerModelMiddleware = registerModelMiddleware;
        _registerVirtualModel = registerVirtualModel;
        _registerSkillSource = registerSkillSource;
        _registerCommandSource = registerCommandSource;
        _findService = findService;
        _findCoreService = findCoreService;
    }

    public string Id { get; }

    public IExtensionSettings Settings
    {
        get
        {
            ThrowIfDisposed();
            return _settings;
        }
    }

    public IExtensionSecrets Secrets
    {
        get
        {
            ThrowIfDisposed();
            return _secrets;
        }
    }

    public IExtensionLog Log { get; }

    /// <summary>
    /// Marks the context disposed: the extension was unloaded, so a host call from a handler that
    /// outlived it throws <see cref="ObjectDisposedException"/> instead of reaching a host that has
    /// already reported the extension gone (design D3, sign-off Q2). The id and the log stay
    /// usable — a diagnostic must never become the failure it describes.
    /// </summary>
    internal void MarkDisposed() => _disposed = true;

    public void Register(IHookHandler handler)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(handler);
        _register?.Invoke(Id, handler);
    }

    public void RegisterService(string name, IBackgroundService service)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(service);
        _registerService?.Invoke(Id, name, service);
    }

    public void SubscribeFileChanged(IFileChangedHandler handler, string? pathPattern = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(handler);
        _subscribeFileChanged?.Invoke(Id, handler, pathPattern);
    }

    public void RegisterToolResultRenderer(IToolResultRenderer renderer)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(renderer);
        _registerToolResultRenderer?.Invoke(Id, renderer);
    }

    public void RegisterStatusWidget(IStatusWidget widget)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(widget);
        _registerStatusWidget?.Invoke(Id, widget);
    }

    public void RegisterTool(ExtensionToolRegistration registration)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(registration);
        _registerTool?.Invoke(Id, registration);
    }

    public void RegisterMcpServer(McpServerRegistration registration)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(registration);
        _registerMcpServer?.Invoke(Id, registration);
    }

    public void RegisterModelProvider(ExtensionModelProvider provider)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(provider);
        _registerModelProvider?.Invoke(Id, provider);
    }

    public void RegisterModelMiddleware(ExtensionModelMiddleware middleware)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(middleware);
        _registerModelMiddleware?.Invoke(Id, middleware);
    }

    public void RegisterVirtualModel(VirtualModelRegistration model)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(model);
        _registerVirtualModel?.Invoke(Id, model);
    }

    public void RegisterSkillSource(ISkillSource source)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(source);
        _registerSkillSource?.Invoke(Id, source);
    }

    public void RegisterCommandSource(ICommandSource source)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(source);
        _registerCommandSource?.Invoke(Id, source);
    }

    public bool TryGetService(string name, out IBackgroundService? service)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        service = _findService?.Invoke(name);
        return service is not null;
    }

    public bool TryGetCoreService<T>(string name, out T? service)
        where T : class
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        service = _findCoreService?.Invoke(name) as T;
        return service is not null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                Id,
                $"extension '{Id}' was unloaded; its host context is disposed."
            );
        }
    }
}
