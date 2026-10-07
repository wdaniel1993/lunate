using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

internal sealed class NullExtensionLog : IExtensionLog
{
    public static NullExtensionLog Instance { get; } = new();

    public void Info(string message) { }

    public void Warn(string message) { }

    public void Error(string message) { }
}
