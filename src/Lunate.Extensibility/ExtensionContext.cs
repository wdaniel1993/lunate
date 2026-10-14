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
    private readonly Func<string, IBackgroundService?>? _findService;
    private readonly Func<string, object?>? _findCoreService;

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
        Action<string, VirtualModelRegistration>? registerVirtualModel = null
    )
    {
        Id = id;
        Settings = settings;
        Secrets = secrets;
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
        _findService = findService;
        _findCoreService = findCoreService;
    }

    public string Id { get; }

    public IExtensionSettings Settings { get; }

    public IExtensionSecrets Secrets { get; }

    public IExtensionLog Log { get; }

    public void Register(IHookHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _register?.Invoke(Id, handler);
    }

    public void RegisterService(string name, IBackgroundService service)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(service);
        _registerService?.Invoke(Id, name, service);
    }

    public void SubscribeFileChanged(IFileChangedHandler handler, string? pathPattern = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _subscribeFileChanged?.Invoke(Id, handler, pathPattern);
    }

    public void RegisterToolResultRenderer(IToolResultRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _registerToolResultRenderer?.Invoke(Id, renderer);
    }

    public void RegisterStatusWidget(IStatusWidget widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        _registerStatusWidget?.Invoke(Id, widget);
    }

    public void RegisterTool(ExtensionToolRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registerTool?.Invoke(Id, registration);
    }

    public void RegisterMcpServer(McpServerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registerMcpServer?.Invoke(Id, registration);
    }

    public void RegisterModelProvider(ExtensionModelProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _registerModelProvider?.Invoke(Id, provider);
    }

    public void RegisterModelMiddleware(ExtensionModelMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _registerModelMiddleware?.Invoke(Id, middleware);
    }

    public void RegisterVirtualModel(VirtualModelRegistration model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _registerVirtualModel?.Invoke(Id, model);
    }

    public bool TryGetService(string name, out IBackgroundService? service)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        service = _findService?.Invoke(name);
        return service is not null;
    }

    public bool TryGetCoreService<T>(string name, out T? service)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        service = _findCoreService?.Invoke(name) as T;
        return service is not null;
    }
}
