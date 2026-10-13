using System.Text.Json;
using HelloExtension.Support;
using Lunate.Extensibility.Abstractions;
using Microsoft.Extensions.AI;

namespace HelloExtension;

public sealed class HelloExtension : IExtension
{
    public HelloExtension(string greeting) => Greeting = greeting;

    public string Greeting { get; }
}

public sealed class HelloExtensionFactory : IExtensionFactory
{
    private static int _created;
    private readonly SupportGreeter _greeter = new();

    public IExtension Create(IExtensionContext context)
    {
        int instance = Interlocked.Increment(ref _created);
        if (
            context.Settings.TryGet("markerPath", out JsonElement marker)
            && marker.ValueKind == JsonValueKind.String
        )
        {
            File.WriteAllText(marker.GetString()!, $"{context.Id} factory ran");
        }

        bool secretPresent = context.Secrets.TryGet("token", out _);
        string shared = $"{JsonSerializer.Serialize(context.Id)}/{typeof(ChatMessage).Name}";
        context.Log.Info(
            $"created {context.Id} instance {instance}: {_greeter.Greeting}, secret present: {secretPresent}, shared {shared}"
        );
        context.Register(new HelloSessionStartedHandler(context.Log));
        context.Register(new HelloSessionEndingHandler(context.Log));
        context.Register(new HelloToolCallingHandler(context.Log));
        context.RegisterToolResultRenderer(new HelloToolResultRenderer(context.Log));
        context.RegisterStatusWidget(new HelloStatusWidget(context.Log));
        context.RegisterService(
            $"ext/{context.Id}/greeter",
            new HelloBackgroundService(context.Log)
        );
        context.SubscribeFileChanged(new HelloFileChangedHandler(context.Log), "*.cs");
        bool hasBus = context.TryGetCoreService<IFileChangeBus>("core/file-bus", out _);
        bool hasQueue = context.TryGetCoreService<IMutationQueue>("core/mutation-queue", out _);
        bool hasService = context.TryGetService($"ext/{context.Id}/greeter", out _);
        context.Log.Info($"hello lookups: bus={hasBus}, queue={hasQueue}, service={hasService}");
        return new HelloExtension(_greeter.Greeting);
    }
}
