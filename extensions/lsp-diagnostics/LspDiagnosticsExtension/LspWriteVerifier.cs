using System.Globalization;
using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace LspDiagnosticsExtension;

/// <summary>
/// The write-verifier face of the session's language server: the host calls it between read and
/// normalize and apply and write, after approval, and the answer comes from the server the
/// extension already runs — verification never starts a process of its own. An error diagnostic in
/// the staged text refuses with the typed diagnostics; a slow, absent or crashed server degrades to
/// unverified with a reason — never a silent pass, never a blocked turn.
/// </summary>
public sealed class LspWriteVerifier(
    string extensionId,
    IExtensionLog log,
    LspServerService server,
    LspSettings settings
) : IWriteVerifier
{
    public async ValueTask<WriteVerificationResult> VerifyAsync(
        WriteVerificationRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        string absolutePath = Path.GetFullPath(
            Path.Combine(request.Workspace.WorktreeRoot, request.RelativePath)
        );
        IReadOnlyList<Diagnostic> diagnostics;
        try
        {
            diagnostics = await server
                .GetDiagnosticsAsync(
                    absolutePath,
                    request.StagedContent,
                    request.Workspace.WorktreeRoot,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            string reason = string.Create(
                CultureInfo.InvariantCulture,
                $"the language server could not verify '{request.RelativePath}': {exception.Message}; the write proceeds without verification"
            );
            log.Error(
                $"lsp-diagnostics[{extensionId}]: verification of '{request.RelativePath}' degraded to unverified: {exception.GetType().Name}: {exception.Message}."
            );
            return new WriteVerificationResult(WriteVerificationStatus.Unverified, reason);
        }

        List<Diagnostic> errors = [.. diagnostics.Where(diagnostic => diagnostic.Severity == 1)];
        if (errors.Count == 0)
        {
            log.Info(
                $"lsp-diagnostics[{extensionId}]: verification of '{request.RelativePath}': verified."
            );
            return new WriteVerificationResult(WriteVerificationStatus.Verified);
        }

        (_, JsonElement structured) = DiagnosticsText.Shape(
            request.RelativePath,
            settings.Command,
            settings.MaxDiagnostics,
            errors
        );
        int count = errors.Count;
        string word = count == 1 ? "error" : "errors";
        string label = string.Create(
            CultureInfo.InvariantCulture,
            $"lsp-diagnostics[{extensionId}]: verification of '{request.RelativePath}': refused with {count} {word}."
        );
        log.Info(label);
        string refusal = string.Create(
            CultureInfo.InvariantCulture,
            $"the language server reported {count} {word} in the staged content for '{request.RelativePath}'; review them in the structured diagnostics and fix them, then write again"
        );
        return new WriteVerificationResult(WriteVerificationStatus.Refused, refusal, structured);
    }
}
