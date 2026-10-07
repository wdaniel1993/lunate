using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

internal sealed class RecordingExtensionLog : IExtensionLog
{
    public List<string> Messages { get; } = [];

    public void Info(string message) => Messages.Add($"info: {message}");

    public void Warn(string message) => Messages.Add($"warn: {message}");

    public void Error(string message) => Messages.Add($"error: {message}");
}
