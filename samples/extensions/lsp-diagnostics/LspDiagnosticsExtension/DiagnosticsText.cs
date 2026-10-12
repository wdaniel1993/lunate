using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LspDiagnosticsExtension;

/// <summary>
/// Shapes filtered diagnostics into the model-facing pair: a labeled text section appended to the
/// tool result and the same capped copy as typed structured content. Positions convert from LSP's
/// 0-based units to 1-based exactly once, here.
/// </summary>
internal static class DiagnosticsText
{
    public static (string Text, JsonElement Structured) Shape(
        string path,
        string source,
        int maxDiagnostics,
        IReadOnlyList<Diagnostic> diagnostics
    )
    {
        int total = diagnostics.Count;
        int shown = Math.Clamp(maxDiagnostics, 0, total);
        var builder = new StringBuilder();
        builder.Append(
            CultureInfo.InvariantCulture,
            $"lsp-diagnostics ({source}): {total} {(total == 1 ? "diagnostic" : "diagnostics")} for {path}"
        );
        for (int index = 0; index < shown; index++)
        {
            Diagnostic diagnostic = diagnostics[index];
            builder.Append('\n');
            builder.Append(
                CultureInfo.InvariantCulture,
                $"{SeverityName(diagnostic.Severity)} {diagnostic.Line + 1}:{diagnostic.Character + 1} {diagnostic.Message} ({diagnostic.Code})"
            );
        }

        if (total > shown)
        {
            builder.Append('\n');
            builder.Append(CultureInfo.InvariantCulture, $"+{total - shown} more diagnostics");
        }

        JsonElement structured = JsonSerializer.SerializeToElement(
            new
            {
                path,
                diagnostics = diagnostics
                    .Take(shown)
                    .Select(diagnostic => new
                    {
                        severity = SeverityName(diagnostic.Severity),
                        line = diagnostic.Line + 1,
                        character = diagnostic.Character + 1,
                        code = diagnostic.Code,
                        message = diagnostic.Message,
                    })
                    .ToArray(),
            }
        );
        return (builder.ToString(), structured);
    }

    private static string SeverityName(int severity) => severity == 1 ? "error" : "warning";
}
