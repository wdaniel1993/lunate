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

public sealed class HelloToolCallingHandler(IExtensionLog log) : IToolCallingHandler
{
    public int Priority => 0;

    public ValueTask<ToolCallingResult> HandleAsync(
        ToolCallingPayload payload,
        CancellationToken cancellationToken
    )
    {
        log.Info($"tool call observed: {payload.ToolName}");
        return ValueTask.FromResult<ToolCallingResult>(new ToolCallingResult.Proceed(null));
    }
}
