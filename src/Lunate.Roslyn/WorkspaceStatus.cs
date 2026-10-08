namespace Lunate.Roslyn;

/// <summary>How a workspace load ended; failures are always surfaced as one of these states.</summary>
public enum WorkspaceStatus
{
    /// <summary>Every project loaded.</summary>
    Loaded = 0,

    /// <summary>The solution loaded with workspace failures; diagnostics still work for loaded projects.</summary>
    Partial = 1,

    /// <summary>At least one project has no restore output; the caller must restore before compiling.</summary>
    RestoreRequired = 2,

    /// <summary>No .NET SDK could be located; the message explains what is missing.</summary>
    NoSdk = 3,

    /// <summary>No solution (or no unambiguous solution) was found under the root.</summary>
    NoSolution = 4,
}
