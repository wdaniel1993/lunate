namespace Lunate.Agent;

/// <summary>
/// Stamps the run's workspace id and run id onto every event and forwards it: the harness binds
/// one per run, so tools publish with the values the run stamped and never with session-level
/// guesses.
/// </summary>
internal sealed class RunFileChangeSink(
    IFileChangeSink inner,
    string workspaceId,
    string runId,
    IReadLedger reads
) : IFileChangeSink
{
    public void Notify(FileChangeEvent change)
    {
        FileChangeEvent stamped = change with { WorkspaceId = workspaceId, RunId = runId };
        reads.Invalidate(stamped.AbsolutePath);
        inner.Notify(stamped);
    }
}
