using Lunate.Extensibility.Abstractions;

namespace Lunate.Roslyn;

internal sealed class CSharpWriteVerifier(Func<WorkspaceInfo, RoslynBackend?> backendFor)
    : IWriteVerifier
{
    public ValueTask<WriteVerificationResult> VerifyAsync(
        WriteVerificationRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        RoslynBackend? backend = backendFor(request.Workspace);
        if (backend is null)
        {
            return ValueTask.FromResult(
                new WriteVerificationResult(
                    WriteVerificationStatus.Unverified,
                    "workspace-not-loaded: call a cs_* tool to load the C# workspace before asking for compiler verification"
                )
            );
        }

        string absolutePath = Path.GetFullPath(
            Path.Combine(request.Workspace.WorktreeRoot, request.RelativePath)
        );
        return backend.VerifyStagedTextAsync(
            absolutePath,
            request.BaselineContent,
            request.StagedContent,
            cancellationToken
        );
    }
}
