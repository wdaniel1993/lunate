using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

internal sealed class ExtensionContext : IExtensionContext
{
    private readonly Action<string, IHookHandler>? _register;
    private readonly Action<string, string, IBackgroundService>? _registerService;
    private readonly Action<string, IFileChangedHandler, string?>? _subscribeFileChanged;
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
        Func<string, object?>? findCoreService = null
    )
    {
        Id = id;
        Settings = settings;
        Secrets = secrets;
        Log = log;
        _register = register;
        _registerService = registerService;
        _subscribeFileChanged = subscribeFileChanged;
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
