using Lunate.Extensibility.Abstractions;

namespace HelloExtension;

public sealed class HelloSessionStartedHandler(IExtensionLog log) : ISessionStartedHandler
{
    public int Priority => 0;

    public ValueTask HandleAsync(SessionStartedPayload payload, CancellationToken cancellationToken)
    {
        log.Info($"hello session started in {payload.WorkingDirectory}");
        return ValueTask.CompletedTask;
    }
}

public sealed class HelloSessionEndingHandler(IExtensionLog log) : ISessionEndingHandler
{
    public int Priority => 0;

    public ValueTask HandleAsync(SessionEndingPayload payload, CancellationToken cancellationToken)
    {
        log.Info($"hello session ended in {payload.WorkingDirectory}");
        return ValueTask.CompletedTask;
    }
}
