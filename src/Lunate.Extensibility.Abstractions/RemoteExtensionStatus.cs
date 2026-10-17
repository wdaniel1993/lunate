namespace Lunate.Extensibility.Abstractions;

/// <summary>Where one remote extension is in its lifecycle.</summary>
public enum RemoteExtensionState
{
    /// <summary>Proxies are built; the process has not started (the lazy default).</summary>
    NotStarted,

    /// <summary>The process is starting and initializing.</summary>
    Starting,

    /// <summary>The handshake succeeded; calls cross the wire.</summary>
    Ready,

    /// <summary>The process exited (session continues); a manual restart is available.</summary>
    Failed,

    /// <summary>The handshake refused the extension (version mismatch or undeclared surface).</summary>
    Refused,

    /// <summary>The session ended and the process was stopped.</summary>
    Stopped,
}

/// <summary>The current lifecycle status and its reason (the <c>/extensions</c> view's data).</summary>
public sealed record RemoteExtensionStatus(RemoteExtensionState State, string? Reason)
{
    /// <summary>True when the extension is no longer serving calls (crash or refusal).</summary>
    public bool IsFailed => State is RemoteExtensionState.Failed or RemoteExtensionState.Refused;
}
