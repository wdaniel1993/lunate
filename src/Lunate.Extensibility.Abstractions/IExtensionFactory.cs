namespace Lunate.Extensibility.Abstractions;

public interface IExtensionFactory
{
    IExtension Create(IExtensionContext context);
}
