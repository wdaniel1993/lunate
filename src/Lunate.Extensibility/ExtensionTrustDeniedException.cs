namespace Lunate.Extensibility;

public sealed class ExtensionTrustDeniedException : Exception
{
    public ExtensionTrustDeniedException(string message)
        : base(message) { }

    public ExtensionTrustDeniedException(string message, Exception innerException)
        : base(message, innerException) { }
}
