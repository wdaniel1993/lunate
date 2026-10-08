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

    /// <summary>
    /// Finds definitions for a simple name or a dotted container path (for example
    /// <c>Calculator</c> or <c>CalculatorLib.Calculator.Add</c>). Case-sensitive and deterministic;
    /// an empty name asks for a name, an unknown name is an empty result with a hint, and failures
    /// return a status instead of throwing.
    /// </summary>
    ValueTask<SymbolSearchResult> FindSymbolAsync(string name, CancellationToken ct);

    /// <summary>
    /// Lists the usage sites of an exactly resolved symbol: a simple name only when it is unique,
    /// a dotted container path to disambiguate. Ambiguous names return candidates instead of
    /// guessing, metadata symbols return a message, and the declaration site is reported once in
    /// <see cref="ReferencesResult.Resolved"/>. Failures return a status instead of throwing.
    /// </summary>
    ValueTask<ReferencesResult> FindReferencesAsync(string name, CancellationToken ct);

    /// <summary>
    /// Lists one file's types and member signatures without bodies, straight from the syntax tree.
    /// Works without a loaded solution; a missing or unreadable file returns an actionable message.
    /// </summary>
    ValueTask<OutlineResult> OutlineAsync(string file, CancellationToken ct);

    /// <summary>
    /// Plans a solution-wide rename and returns the proposed per-file line edits. Nothing is
    /// applied: the forked solution is compared only, the workspace and disk stay untouched, and
    /// the plan is applied through the edit path. Failures return a status instead of throwing.
    /// </summary>
    ValueTask<RenamePlanResult> PlanRenameAsync(string name, string newName, CancellationToken ct);

    /// <summary>Marks a file dirty so the next diagnostics run re-reads it immediately.</summary>
    void NotifyFileChanged(string absolutePath);
}
