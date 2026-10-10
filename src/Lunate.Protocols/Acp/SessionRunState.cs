namespace Lunate.Protocols.Acp;

/// <summary>
/// The run state of one ACP session: the active run's cancellation source, the dispatched prompt
/// count and the pending-cancel latch. One gate keeps "cancel the active run, else latch onto a
/// queued prompt" atomic against a run beginning. Shared with the client file access, which reads
/// <see cref="Token"/> so a session cancel stops an in-flight file round trip.
/// </summary>
internal sealed class SessionRunState
{
    private readonly object gate = new();
    private CancellationTokenSource? active;
    private int prompts;
    private bool cancelPending;

    /// <summary>
    /// The active run's token; <see cref="CancellationToken.None"/> when no run is in flight. The
    /// client file access passes it into the LibAcp file requests, so <see cref="Cancel"/> reaches
    /// an in-flight file round trip through the same token as the run.
    /// </summary>
    public CancellationToken Token
    {
        get
        {
            lock (gate)
            {
                return active?.Token ?? CancellationToken.None;
            }
        }
    }

    public void BeginPrompt()
    {
        lock (gate)
        {
            prompts++;
        }
    }

    public void EndPrompt()
    {
        lock (gate)
        {
            prompts--;
            if (prompts == 0)
            {
                cancelPending = false;
            }
        }
    }

    public void BeginRun(CancellationTokenSource cancellation)
    {
        bool cancel;
        lock (gate)
        {
            active = cancellation;
            cancel = cancelPending;
            cancelPending = false;
        }

        if (cancel)
        {
            cancellation.Cancel();
        }
    }

    public void EndRun()
    {
        lock (gate)
        {
            active = null;
        }
    }

    /// <summary>
    /// Cancels the in-flight run, or latches onto a dispatched prompt that is still queued
    /// (consumed by <see cref="BeginRun"/>, so the run begins already cancelled). A cancel
    /// with no prompt in flight stays a no-op.
    /// </summary>
    public void Cancel()
    {
        lock (gate)
        {
            if (active is not null)
            {
                active.Cancel();
            }
            else if (prompts > 0)
            {
                cancelPending = true;
            }
        }
    }
}
