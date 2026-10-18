using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace ServiceConsumerExtension;

/// <summary>The fixture extension consuming another extension's registered service: at session
/// start (after every extension has loaded) and when a run starts, it looks a provider's service
/// up by name through the shared registry and logs the outcome (with an absent-name control), so
/// the harness can prove cross-extension consumption through the real composition. The
/// <c>slowSessionStartedMs</c> setting parks the session-started handler on a cancellable delay so
/// tests can cancel mid-dispatch.</summary>
public sealed class ServiceConsumerExtension : IExtension { }

public sealed class ServiceConsumerExtensionFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        int? slowSessionStartedMs = null;
        if (
            context.Settings.TryGet("slowSessionStartedMs", out JsonElement slow)
            && slow.ValueKind == JsonValueKind.Number
        )
        {
            slowSessionStartedMs = slow.GetInt32();
        }

        context.Register(new ConsumerSessionStartedHandler(context, slowSessionStartedMs));
        context.Register(new ConsumerRunStartingHandler(context));
        return new ServiceConsumerExtension();
    }
}

internal sealed class ConsumerSessionStartedHandler(
    IExtensionContext context,
    int? slowSessionStartedMs = null
) : ISessionStartedHandler
{
    public int Priority => 0;

    public async ValueTask HandleAsync(
        SessionStartedPayload payload,
        CancellationToken cancellationToken
    )
    {
        if (slowSessionStartedMs is int slow)
        {
            context.Log.Info("consumer slow session-started entered");
            await Task.Delay(slow, cancellationToken).ConfigureAwait(false);
            context.Log.Info("consumer slow session-started completed");
        }

        bool provider = context.TryGetService("ext/hello/greeter", out _);
        bool absent = context.TryGetService("ext/absent/greeter", out _);
        context.Log.Info($"consumer session-started lookups: provider={provider}, absent={absent}");
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
