namespace Lunate.Roslyn;

/// <summary>
/// How a symbol search ended. Mirrors <see cref="WorkspaceStatus"/>: a search still runs on a
/// partial load; the failure states carry the same meanings as a workspace load.
/// </summary>
public enum SymbolSearchStatus
{
    /// <summary>The workspace loaded and the search ran.</summary>
    Loaded = 0,

    /// <summary>The search ran against a partially loaded workspace.</summary>
    Partial = 1,

    /// <summary>At least one project has no restore output; the caller must restore before searching.</summary>
    RestoreRequired = 2,

    /// <summary>No .NET SDK could be located; the message explains what is missing.</summary>
    NoSdk = 3,

    /// <summary>No solution is available (none found, none unambiguous, or nothing loaded yet).</summary>
    NoSolution = 4,
}
