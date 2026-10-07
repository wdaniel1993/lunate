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
}
