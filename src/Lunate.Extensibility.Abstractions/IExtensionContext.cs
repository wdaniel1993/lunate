namespace Lunate.Extensibility.Abstractions;

public interface IExtensionContext
{
    string Id { get; }

    IExtensionSettings Settings { get; }

    IExtensionSecrets Secrets { get; }

    IExtensionLog Log { get; }

    /// <summary>
    /// Registers a hook handler. Registration is registration only: it must not start processes,
    /// sockets or timers.
    /// </summary>
    void Register(IHookHandler handler);

    /// <summary>
    /// Registers a named background service. Registration is registration only; the host starts the
    /// service with session lifecycle semantics. Duplicate names are refused.
    /// </summary>
    void RegisterService(string name, IBackgroundService service);

    /// <summary>Subscribes to file changes after successful mutations, optionally filtered by pattern.</summary>
    void SubscribeFileChanged(IFileChangedHandler handler, string? pathPattern = null);

    /// <summary>Looks up a service another extension registered by its name.</summary>
    bool TryGetService(string name, out IBackgroundService? service);

    /// <summary>Looks up a core service handle by its reserved name (<c>core/…</c>).</summary>
    bool TryGetCoreService<T>(string name, out T? service)
        where T : class;
}
