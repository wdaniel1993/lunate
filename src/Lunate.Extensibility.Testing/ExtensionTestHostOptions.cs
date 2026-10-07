using Lunate.Agent;

namespace Lunate.Extensibility.Testing;

/// <summary>Options for an <see cref="ExtensionTestHost"/>.</summary>
public sealed record ExtensionTestHostOptions
{
    /// <summary>
    /// The caller-owned temp directory every piece of host state lives under. The host creates its
    /// worktree, store and session inside it and never touches the real <c>~/.lunate</c>.
    /// </summary>
    public required string TempDirectory { get; init; }

    /// <summary>The manifest id of the extension under test.</summary>
    public required string ExtensionId { get; init; }

    /// <summary>An existing directory whose files are copied into the temp extension directory.</summary>
    public string? ExtensionDirectory { get; init; }

    /// <summary>Text files (relative path to contents) written into the temp extension directory.</summary>
    public IReadOnlyDictionary<string, string>? SourceFiles { get; init; }

    /// <summary>The queued trust decisions; defaults to one allow.</summary>
    public IReadOnlyList<bool>? TrustDecisions { get; init; }

    /// <summary>The tool approver handed to the harness; null allows every call.</summary>
    public IToolApprover? Approver { get; init; }

    /// <summary>The system prompt sent before the history; null sends none.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>The repository identity the extension is trusted for.</summary>
    public string RepositoryIdentity { get; init; } = "test-repository";

    /// <summary>The maximum number of model calls per run.</summary>
    public int MaxSteps { get; init; } = 8;
}
