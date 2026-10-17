namespace Lunate.Extensibility;

/// <summary>
/// One (extension, hook) timing aggregate for the session: how many dispatches ran, their total,
/// last and maximum milliseconds, and how many of them were slow, completed, timed out or were
/// cancelled by the caller. Cancelled dispatches feed the count, the total and the last value
/// only; a dispatch whose handler failed counts in <see cref="Calls"/> alone (derivable as
/// calls minus completed, timed out and cancelled).
/// </summary>
public sealed record HookTimings(
    string Hook,
    long Calls,
    double TotalMs,
    double LastMs,
    double MaxMs,
    long SlowCount,
    long Completed,
    long TimedOut,
    long Cancelled
);

/// <summary>
/// The first dispatch of a (extension, hook) pair above the slow-hook threshold: the runner logged
/// its one warning and raises this event so a frontend can render one notice. Later crossings only
/// increment the aggregate's slow count.
/// </summary>
public sealed record SlowHookEvent(
    string ExtensionId,
    string Hook,
    TimeSpan Elapsed,
    TimeSpan Threshold
);
