namespace Lunate.Roslyn;

/// <summary>
/// The outcome of <see cref="ICSharpBackend.LoadAsync"/>. <see cref="Failures"/> is bounded (20)
/// while <see cref="TotalFailureCount"/> reports every failure observed.
/// </summary>
public sealed record WorkspaceLoadResult(
    WorkspaceStatus Status,
    string Message,
    string? SolutionPath,
    int ProjectCount,
    int DocumentCount,
    IReadOnlyList<WorkspaceFailure> Failures
)
{
    /// <summary>Total workspace failures observed; <see cref="Failures"/> holds at most the first 20.</summary>
    public int TotalFailureCount { get; init; } = Failures.Count;
}
