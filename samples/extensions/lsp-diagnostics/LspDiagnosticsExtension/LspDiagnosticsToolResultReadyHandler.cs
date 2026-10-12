using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace LspDiagnosticsExtension;

/// <summary>
/// After a successful <c>write</c> or <c>edit</c> of a <c>.cs</c> file, asks the language server
/// for the file's diagnostics and attaches them to the tool result: a labeled text section (errors
/// and warnings only, capped) plus the typed structured content. The path comes from the call's
/// arguments — never parsed from output text — and resolves against the workspace identity. Any
/// failure, and every not-applicable call, passes the original result through unchanged.
/// </summary>
public sealed class LspDiagnosticsToolResultReadyHandler(
    string extensionId,
    IExtensionLog log,
    LspServerService server,
    LspSettings settings,
    WorkspaceInfo? workspace
) : IToolResultReadyHandler
{
    public int Priority => 0;

    public async ValueTask<ToolResultReadyResult> HandleAsync(
        ToolResultReadyPayload payload,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.IsError || payload.ToolName is not ("write" or "edit") || workspace is null)
        {
            return PassThrough(payload);
        }

        if (!TryReadPath(payload.Arguments, out string path))
        {
            return PassThrough(payload);
        }

        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return PassThrough(payload);
        }

        if (!WorkspacePath.TryResolve(workspace.WorktreeRoot, path, out string absolute))
        {
            log.Warn(
                $"lsp-diagnostics[{extensionId}]: '{path}' resolves outside the workspace; nothing was attached."
            );
            return PassThrough(payload);
        }

        string text;
        try
        {
            text = await File.ReadAllTextAsync(absolute, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            log.Error(
                $"lsp-diagnostics[{extensionId}]: could not read '{path}': {exception.Message}; the tool result is unchanged."
            );
            return PassThrough(payload);
        }

        List<Diagnostic> relevant;
        try
        {
            IReadOnlyList<Diagnostic> diagnostics = await server
                .GetDiagnosticsAsync(absolute, text, workspace.WorktreeRoot, cancellationToken)
                .ConfigureAwait(false);
            relevant = [.. diagnostics.Where(diagnostic => diagnostic.IsErrorOrWarning)];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            log.Error(
                $"lsp-diagnostics[{extensionId}]: diagnostics for '{path}' failed: {exception.GetType().Name}: {exception.Message}; the tool result is unchanged."
            );
            return PassThrough(payload);
        }

        if (relevant.Count == 0)
        {
            log.Info(
                $"lsp-diagnostics[{extensionId}]: no errors or warnings for '{path}'; nothing was attached."
            );
            return PassThrough(payload);
        }

        (string section, JsonElement structured) = DiagnosticsText.Shape(
            path,
            settings.Command,
            settings.MaxDiagnostics,
            relevant
        );
        log.Info(
            $"lsp-diagnostics[{extensionId}]: attached {relevant.Count} diagnostics for '{path}'."
        );
        return new ToolResultReadyResult($"{payload.Output}\n\n{section}", null, structured);
    }

    private static ToolResultReadyResult PassThrough(ToolResultReadyPayload payload) =>
        new(payload.Output, null);

    private static bool TryReadPath(JsonElement arguments, out string path)
    {
        path = string.Empty;
        if (
            arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty("path", out JsonElement pathElement)
            || pathElement.ValueKind != JsonValueKind.String
        )
        {
            return false;
        }

        path = pathElement.GetString() ?? string.Empty;
        return path.Length > 0;
    }
}
