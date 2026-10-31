using System.Text.Json;

namespace Lunate.Extensibility.Abstractions;

/// <summary>A read-only request to verify the exact normalized text a host is about to write.</summary>
public sealed class WriteVerificationRequest(
    string relativePath,
    string baselineContent,
    string stagedContent,
    WorkspaceInfo workspace
)
{
    public string RelativePath { get; } = relativePath;

    public string BaselineContent { get; } = baselineContent;

    public string StagedContent { get; } = stagedContent;

    public WorkspaceInfo Workspace { get; } = workspace;
}

/// <summary>The outcome of a write-verifier call.</summary>
public enum WriteVerificationStatus
{
    Verified,
    Unverified,
    Refused,
}

/// <summary>A verification outcome, with optional user/model-facing reason and structured diagnostics.</summary>
public sealed class WriteVerificationResult(
    WriteVerificationStatus status,
    string? reason = null,
    JsonElement? structuredContent = null
)
{
    public WriteVerificationStatus Status { get; } = status;

    public string? Reason { get; } = reason;

    public JsonElement? StructuredContent { get; } = structuredContent;
}

/// <summary>
/// Verifies staged file text without receiving a file handle or any host write capability. The host
/// owns the staged text and decides whether it is applied.
/// </summary>
public interface IWriteVerifier
{
    /// <summary>Verifies the staged text; cancellation is supplied by the host's bounded call.</summary>
    ValueTask<WriteVerificationResult> VerifyAsync(
        WriteVerificationRequest request,
        CancellationToken cancellationToken
    );
}

/// <summary>A verifier name and implementation registered by an extension factory.</summary>
public sealed class WriteVerifierRegistration(string name, IWriteVerifier verifier)
{
    public string Name { get; } = name;

    public IWriteVerifier Verifier { get; } = verifier;
}

/// <summary>A manifest declaration that binds a verifier name to filename patterns.</summary>
public sealed class WriteVerifierDeclaration(string name, IReadOnlyList<string> patterns)
{
    public string Name { get; } = name;

    public IReadOnlyList<string> Patterns { get; } = patterns;
}
