using Microsoft.CodeAnalysis.MSBuild;

namespace Lunate.Roslyn;

/// <summary>One loaded solution: the workspace (kept alive for freshness) and its per-document state.</summary>
internal sealed class WorkspaceEntry(
    MSBuildWorkspace workspace,
    IReadOnlyList<string> documentPaths
) : IDisposable
{
    public MSBuildWorkspace Workspace { get; } = workspace;

    /// <summary>Canonical absolute paths of every document in the solution.</summary>
    public IReadOnlyList<string> DocumentPaths { get; } = documentPaths;

    public DocumentFreshness Freshness { get; } = new();

    public void Dispose() => Workspace.Dispose();
}
