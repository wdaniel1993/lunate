namespace Lunate.Extensibility.Abstractions;

public interface IExtensionContext
{
    string Id { get; }

    IExtensionSettings Settings { get; }

    IExtensionSecrets Secrets { get; }

    IExtensionLog Log { get; }
}
