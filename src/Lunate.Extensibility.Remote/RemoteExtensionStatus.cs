namespace Lunate.Extensibility.Remote;

/// <summary>
/// A remote extension could not start, could not be called, or was refused: the message is a
/// teaching error for the log and the user, never a crash.
/// </summary>
public sealed class RemoteExtensionException : Exception
{
    public RemoteExtensionException(string message)
        : base(message) { }

    public RemoteExtensionException(string message, Exception inner)
        : base(message, inner) { }
}
