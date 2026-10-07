using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

internal sealed class ExtensionContext : IExtensionContext
{
    private readonly Action<string, IHookHandler>? _register;

    public ExtensionContext(
        string id,
        IExtensionSettings settings,
        IExtensionSecrets secrets,
        IExtensionLog log,
        Action<string, IHookHandler>? register = null
    )
    {
        Id = id;
        Settings = settings;
        Secrets = secrets;
        Log = log;
        _register = register;
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
}
