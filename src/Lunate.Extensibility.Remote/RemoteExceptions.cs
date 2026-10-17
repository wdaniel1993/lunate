namespace Lunate.Extensibility.Remote;

/// <summary>
/// The wire refused a frame or a line: a non-JSON stdout line, a JSON array (batches are not
/// supported), a frame that is not a JSON-RPC object, an oversized line, or a malformed result.
/// A protocol error ends the connection; it is never reinterpreted as protocol data.
/// </summary>
public sealed class RemoteProtocolException : Exception
{
    public RemoteProtocolException(string message)
        : base(message) { }

    public RemoteProtocolException(string message, Exception inner)
        : base(message, inner) { }
}

/// <summary>
/// The peer answered a request with a JSON-RPC error object.
/// </summary>
public sealed class RemoteCallException : Exception
{
    public RemoteCallException(int code, string message, string? errorData = null)
        : base($"the extension answered '{message}' (code {code})")
    {
        Code = code;
        PeerMessage = message;
        ErrorData = errorData;
    }

    /// <summary>The JSON-RPC error code.</summary>
    public int Code { get; }

    /// <summary>The peer's error message, unmodified.</summary>
    public string PeerMessage { get; }

    /// <summary>The peer's optional error data.</summary>
    public string? ErrorData { get; }
}

/// <summary>
/// The channel ended (the child exited or the pipe broke) while a call was in flight; in-flight
/// calls fail immediately with this, never by waiting for their timeouts.
/// </summary>
public sealed class RemoteChannelClosedException : Exception
{
    public RemoteChannelClosedException(string message)
        : base(message) { }

    public RemoteChannelClosedException(string message, Exception? inner)
        : base(message, inner) { }
}
