namespace Lunate.Coding;

/// <summary>Where a discovered skill comes from.</summary>
internal enum SkillScope
{
    Project,
    User,
    Extension,
}

/// <summary>One extension's registered skill source as discovery consumes it: the registering
/// extension's id, the extension's own folder (the boundary a contributed skill directory must
/// stay inside) and the skill directories the source named.</summary>
internal sealed record ExtensionSkillSource(
    string ExtensionId,
    string SourceId,
    string ExtensionDirectory,
    IReadOnlyList<string> Directories
);

/// <summary>
/// One discovered skill directory: the parsed frontmatter and body of its <c>SKILL.md</c> plus the
/// resolution state (shadowing, trust, index overflow). Invalid entries carry no body; they are
/// listed, never loaded.
/// </summary>
internal sealed record SkillDescriptor(
    string Name,
    string Description,
    string Body,
    string Directory,
    string SkillFile,
    SkillScope Scope,
    string? ExtensionId,
    int ProjectDepth,
    int SourceRank,
    bool CommandOnly,
    bool Valid,
    string? InvalidReason
)
{
    /// <summary>The scope label of the resolution that shadowed this skill; null when none did.</summary>
    public string? Shadows { get; init; }

    /// <summary>Whether a project skill's repository was not trusted for skills.</summary>
    public bool Untrusted { get; init; }

    /// <summary>Whether index composition cut this skill from the model index (overflow).</summary>
    public bool NotIndexed { get; init; }

    /// <summary>The scope label of this entry as <c>/skills</c> renders it.</summary>
    internal string ScopeLabel =>
        Scope switch
        {
            SkillScope.Project => "project",
            SkillScope.User => "user",
            _ => $"extension '{ExtensionId}'",
        };

    /// <summary>Whether this entry is eligible for the model index (valid, model-visible, trusted).</summary>
    internal bool IndexEligible => Valid && !CommandOnly && !Untrusted;

    /// <summary>The index order: project, then user, then extension, each group by name (ordinal).</summary>
    internal static IComparer<SkillDescriptor> IndexOrderComparer { get; } = new IndexOrder();

    private sealed class IndexOrder : IComparer<SkillDescriptor>
    {
        public int Compare(SkillDescriptor? left, SkillDescriptor? right)
        {
            if (left is null || right is null)
            {
                return left is null ? (right is null ? 0 : -1) : 1;
            }

            int scope = left.Scope.CompareTo(right.Scope);
            return scope != 0 ? scope : string.CompareOrdinal(left.Name, right.Name);
        }
    }
}

/// <summary>The raw discovery input: the boundaries discovery may read and the extension sources.</summary>
internal sealed record SkillScanRequest(
    string WorktreeRoot,
    string? RepoRoot,
    string HomeDirectory,
    IReadOnlyList<ExtensionSkillSource> ExtensionSources
);

/// <summary>The scan outcome: every discovered entry (resolved winners and invalid entries), the
/// one-time warnings and the read-only allowance roots (every discovered skill directory).</summary>
internal sealed record SkillScanResult(
    IReadOnlyList<SkillDescriptor> Skills,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> ReadRoots
);
