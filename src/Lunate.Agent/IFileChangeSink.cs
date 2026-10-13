namespace Lunate.Agent;

/// <summary>The kind of a successful file mutation.</summary>
public enum FileChangeKind
{
    /// <summary>The mutation created a file that did not exist.</summary>
    Created,

    /// <summary>The mutation replaced a file's content (an identical-content write included).</summary>
    Modified,
}

/// <summary>
/// One successful file mutation as a tool reports it: the canonical absolute path, the
/// workspace-relative path, the workspace id (the run's <c>WorktreeRoot</c>), the run id when the
/// mutation ran inside a run, and the change kind.
/// </summary>
public sealed record FileChangeEvent(
    string AbsolutePath,
    string RelativePath,
    string WorkspaceId,
    string? RunId,
    FileChangeKind Kind
);

/// <summary>
/// The file change seam: the host injects a sink (the extension file change bus) into every tool
/// through <see cref="ToolContext.FileChanges"/> and the harness binds the run's workspace id and
/// run id into each run's sink. The harness itself never publishes; a tool calls the sink once
/// after a successful mutation. The core bus never throws into the mutation path.
/// </summary>
public interface IFileChangeSink
{
    /// <summary>Publishes one successful mutation.</summary>
    void Notify(FileChangeEvent change);
}

/// <summary>The default sink: mutations emit nothing.</summary>
public sealed class NullFileChangeSink : IFileChangeSink
{
    private NullFileChangeSink() { }

    public static NullFileChangeSink Instance { get; } = new();

    public void Notify(FileChangeEvent change) { }
}

/// <summary>
/// Stamps the run's workspace id and run id onto every event and forwards it: the harness binds
/// one per run, so tools publish with the values the run stamped and never with session-level
/// guesses.
/// </summary>
internal sealed class RunFileChangeSink(IFileChangeSink inner, string workspaceId, string runId)
    : IFileChangeSink
{
    public void Notify(FileChangeEvent change) =>
        inner.Notify(change with { WorkspaceId = workspaceId, RunId = runId });
}
