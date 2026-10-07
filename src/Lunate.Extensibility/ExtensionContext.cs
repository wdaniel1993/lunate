using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

internal sealed class ExtensionContext : IExtensionContext
{
    public ExtensionContext(
        string id,
        IExtensionSettings settings,
        IExtensionSecrets secrets,
        IExtensionLog log
    )
    {
        Id = id;
        Settings = settings;
        Secrets = secrets;
        Log = log;
    }

    public string Id { get; }

    public IExtensionSettings Settings { get; }

    public IExtensionSecrets Secrets { get; }

    public IExtensionLog Log { get; }
}
