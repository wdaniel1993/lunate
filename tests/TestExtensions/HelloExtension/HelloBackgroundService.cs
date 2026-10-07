using Lunate.Extensibility.Abstractions;

namespace HelloExtension;

public sealed class HelloBackgroundService(IExtensionLog log) : IBackgroundService
{
    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        log.Info("hello service started");
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        log.Info("hello service stopped");
        return ValueTask.CompletedTask;
    }
}
