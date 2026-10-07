using Lunate.Extensibility.Abstractions;

namespace HelloExtension.Support;

public sealed class SupportGreeter
{
    public string Greeting => "hello from support";
}

public sealed class FirstFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context) => new EmptyExtension();
}

public sealed class SecondFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context) => new EmptyExtension();
}

internal sealed class EmptyExtension : IExtension { }
