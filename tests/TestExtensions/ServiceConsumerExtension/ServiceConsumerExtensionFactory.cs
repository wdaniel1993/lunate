using Lunate.Extensibility.Abstractions;

namespace ServiceConsumerExtension;

/// <summary>The fixture extension consuming another extension's registered service: at session
/// start (after every extension has loaded) and when a run starts, it looks a provider's service
/// up by name through the shared registry and logs the outcome (with an absent-name control), so
/// the harness can prove cross-extension consumption through the real composition.</summary>
public sealed class ServiceConsumerExtension : IExtension { }

public sealed class ServiceConsumerExtensionFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Register(new ConsumerSessionStartedHandler(context));
        context.Register(new ConsumerRunStartingHandler(context));
        return new ServiceConsumerExtension();
    }
}

internal sealed class ConsumerSessionStartedHandler(IExtensionContext context)
    : ISessionStartedHandler
{
    public int Priority => 0;

    public ValueTask HandleAsync(SessionStartedPayload payload, CancellationToken cancellationToken)
    {
        bool provider = context.TryGetService("ext/hello/greeter", out _);
        bool absent = context.TryGetService("ext/absent/greeter", out _);
        context.Log.Info($"consumer session-started lookups: provider={provider}, absent={absent}");
        return ValueTask.CompletedTask;
    }
}

internal sealed class ConsumerRunStartingHandler(IExtensionContext context) : IRunStartingHandler
{
    public int Priority => 0;

    public ValueTask<RunStartingResult> HandleAsync(
        RunStartingPayload payload,
        CancellationToken cancellationToken
    )
    {
        bool provider = context.TryGetService("ext/hello/greeter", out _);
        bool absent = context.TryGetService("ext/absent/greeter", out _);
        context.Log.Info($"consumer cross-service lookups: provider={provider}, absent={absent}");
        return ValueTask.FromResult<RunStartingResult>(new RunStartingResult.None());
    }
}
