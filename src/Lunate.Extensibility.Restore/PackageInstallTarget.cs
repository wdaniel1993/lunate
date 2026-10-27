namespace Lunate.Extensibility.Restore;

/// <summary>
/// Where one install writes (design D6): the scope root every recorded path is relative to, the
/// package's own directory under the state root — the record and its staging live there — and the
/// content roots the extension and the skills land in. A user install puts content under the state
/// root; a project install puts it in the worktree while the record stays machine state.
/// </summary>
internal sealed record PackageInstallTarget
{
    /// <summary>Every path in the record is relative to this directory.</summary>
    public required string ScopeRoot { get; init; }

    /// <summary><c>&lt;root&gt;/packages/&lt;package-id&gt;/</c>: the record, and staging beside it.</summary>
    public required string RecordDirectory { get; init; }

    public required PackageScope Scope { get; init; }

    /// <summary>The worktree a project install belongs to; null for a user install.</summary>
    public string? Worktree { get; init; }

    /// <summary>The directory that holds <c>&lt;extension-id&gt;/</c>, relative to the scope root.</summary>
    public string ExtensionDirectory { get; init; } = "extensions";

    /// <summary>The directory that holds <c>&lt;skill-name&gt;/</c>, relative to the scope root.</summary>
    public string SkillsDirectory { get; init; } = "skills";
    public string RecordPath => Path.Combine(RecordDirectory, PackageInstallRecordJson.FileName);

    /// <summary>
    /// The state root the record lives under. The record directory is
    /// <c>&lt;root&gt;/packages/&lt;package-id&gt;/</c>, so the root is its grandparent; the install
    /// reads it to find the other scope's content without carrying a second copy of the same path.
    /// </summary>
    public string StoreRoot =>
        Path.GetDirectoryName(Path.GetDirectoryName(RecordDirectory))
        ?? throw new InvalidOperationException(
            $"the record directory '{RecordDirectory}' is not under a state root."
        );

    /// <summary>
    /// A staging directory for one operation alone: two installs never share one, so neither can
    /// extract over, or delete, a file the other is still staging. Named without a fixed spelling so
    /// a crashed run's leftover is distinguishable from a live one and cleared under the claim
    /// before the next run stages anything.
    /// </summary>
    internal string NewStagingDirectory() =>
        Path.Combine(RecordDirectory, "staging-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// A user-scope install: the record, the extension and the skills all under the state root, the
    /// layout every global extension and user skill already has.
    /// </summary>
    public static PackageInstallTarget ForUser(string storeRoot, string packageId) =>
        new()
        {
            ScopeRoot = Absolute(storeRoot),
            RecordDirectory = PackageDirectory(storeRoot, packageId),
            Scope = PackageScope.User,
        };

    /// <summary>
    /// A project-scope install: the extension in <c>&lt;worktree&gt;/.lunate/extensions/</c> and the
    /// skills in <c>&lt;worktree&gt;/.agents/skills/</c>, so the content travels with the repository,
    /// while the record stays under the state root and names the worktree.
    /// </summary>
    public static PackageInstallTarget ForProject(
        string worktreeRoot,
        string storeRoot,
        string packageId
    ) =>
        new()
        {
            ScopeRoot = Absolute(worktreeRoot),
            RecordDirectory = PackageDirectory(storeRoot, packageId),
            Scope = PackageScope.Project,
            Worktree = Absolute(worktreeRoot),
            ExtensionDirectory = ProjectScope.ExtensionsDirectory,
            SkillsDirectory = ProjectScope.SkillsDirectory,
        };

    /// <summary>
    /// NuGet ids are case-insensitive and two of three filesystems agree, so the package's directory
    /// is the lowercased id; the record keeps the canonical spelling.
    /// </summary>
    private static string PackageDirectory(string storeRoot, string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return Path.Combine(Absolute(storeRoot), "packages", packageId.ToLowerInvariant());
    }

    private static string Absolute(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
