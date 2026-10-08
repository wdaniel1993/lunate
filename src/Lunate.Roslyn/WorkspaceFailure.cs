namespace Lunate.Roslyn;

/// <summary>
/// One workspace-level failure (project evaluation, references, workloads). <see cref="Project"/>
/// names the project when known; the message is the vendor diagnostic text.
/// </summary>
public sealed record WorkspaceFailure(string? Project, string Message);
