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
        bool secretPresent = context.Secrets.TryGet("token", out _);
        string shared = $"{JsonSerializer.Serialize(context.Id)}/{typeof(ChatMessage).Name}";
        context.Log.Info(
            $"created {context.Id} instance {instance}: {_greeter.Greeting}, secret present: {secretPresent}, shared {shared}"
        );
        context.Register(new HelloSessionStartedHandler(context.Log));
        context.Register(new HelloSessionEndingHandler(context.Log));
        return new HelloExtension(_greeter.Greeting);
    }
}
