namespace Lunate.Extensibility;

public sealed class ExtensionLoadException : Exception
{
    public ExtensionLoadException(string message)
        : base(message) { }

    public ExtensionLoadException(string message, Exception innerException)
        : base(message, innerException) { }
}
