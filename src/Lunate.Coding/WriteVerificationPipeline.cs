using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

internal static class WriteVerificationPipeline
{
    internal static async Task<ToolResult?> RefuseIfNeededAsync(
        Func<string, IWriteVerifier?>? verifierSelector,
        string relativePath,
        string baselineContent,
        string stagedContent,
        WorkspaceInfo workspace,
        CancellationToken ct
    )
    {
        IWriteVerifier? verifier = verifierSelector?.Invoke(relativePath);
        if (verifier is null)
        {
            return null;
        }

        WriteVerificationResult result = await verifier
            .VerifyAsync(
                new WriteVerificationRequest(relativePath, baselineContent, stagedContent, workspace),
                ct
            )
            .ConfigureAwait(false);

        return result.Status is WriteVerificationStatus.Refused
            ? new ToolResult(
                result.Reason
                    ?? "The write verifier refused the staged content; review its structured diagnostics and correct the errors before retrying.",
                IsError: true,
                StructuredContent: result.StructuredContent
            )
            : null;
    }
}
