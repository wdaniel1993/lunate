namespace Lunate.Roslyn;

/// <summary>
/// The C# engine behind the C# tools (ADR-0014). Operations are name/scope based; the backend
/// never writes project files and every failure path returns a result instead of throwing.
/// </summary>
public interface ICSharpBackend
{
    /// <summary>
    /// Loads the solution discovered under the backend's worktree root. Idempotent: an already
    /// loaded solution returns the same result.
    /// </summary>
    ValueTask<WorkspaceLoadResult> LoadAsync(CancellationToken ct);

    /// <summary>Reports compiler errors and warnings for the requested scope.</summary>
    ValueTask<DiagnosticsResult> GetDiagnosticsAsync(DiagnosticsScope scope, CancellationToken ct);

    /// <summary>Marks a file dirty so the next diagnostics run re-reads it immediately.</summary>
    void NotifyFileChanged(string absolutePath);
}
