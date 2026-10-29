namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// One manifest <c>toolActivation</c> rule. <paramref name="when"/> is the closed condition name
/// (<c>always</c>, <c>workspace-root-has-file</c> or <c>workspace-is-git-repository</c>) and
/// <paramref name="patterns"/> are the direct-child file names of
/// <c>workspace-root-has-file</c> — empty for the conditions that take no fields. The manifest
/// parser produces these rules; the host evaluates them (the evaluator lives in the same assembly).
/// </summary>
public sealed record ExtensionToolActivationRule(string When, IReadOnlyList<string> Patterns)
{
    /// <summary>The condition that never contradicts availability.</summary>
    public const string Always = "always";

    /// <summary>The condition matching when a direct child of the workspace root has one of the
    /// rule's file names.</summary>
    public const string WorkspaceRootHasFile = "workspace-root-has-file";

    /// <summary>The condition matching when the workspace is a git repository.</summary>
    public const string WorkspaceIsGitRepository = "workspace-is-git-repository";
}
