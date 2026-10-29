using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace LspDiagnosticsExtension;

internal sealed partial class LspClient
{
    /// <summary>
    /// Waits for the publish carrying the sent version; stale versions are ignored and the wait
    /// continues under the caller's bound. A closed process channel surfaces as an IO failure.
    /// </summary>
    private async Task<IReadOnlyList<Diagnostic>> WaitForDiagnosticsAsync(
        string uri,
        int version,
        CancellationToken ct
    )
    {
        while (true)
        {
            Publish published;
            try
            {
                published = await _published.Reader.ReadAsync(ct);
            }
            catch (ChannelClosedException) when (_readError is { } error)
            {
                throw new IOException(
                    $"the language server exited before publishing diagnostics: {error.Message}",
                    error
                );
            }
            catch (ChannelClosedException)
            {
                throw new IOException("the language server exited before publishing diagnostics.");
            }

            if (
                string.Equals(published.Uri, uri, StringComparison.Ordinal)
                && published.Version == version
            )
            {
                return published.Diagnostics;
            }
        }
    }

    private static List<Diagnostic> ParseDiagnostics(JsonNode? parameters)
    {
        var diagnostics = new List<Diagnostic>();
        if (parameters?["diagnostics"] is not JsonArray array)
        {
            return diagnostics;
        }

        foreach (JsonNode? node in array)
        {
            if (node is null)
            {
                continue;
            }

            int severity = node["severity"]?.GetValue<int>() ?? 0;
            int line = node["range"]?["start"]?["line"]?.GetValue<int>() ?? 0;
            int character = node["range"]?["start"]?["character"]?.GetValue<int>() ?? 0;
            string code = node["code"]?.ToString() ?? string.Empty;
            string message = node["message"]?.GetValue<string>() ?? string.Empty;
            diagnostics.Add(new Diagnostic(severity, line, character, code, message));
        }

        return diagnostics;
    }

    private sealed record Publish(string Uri, int? Version, IReadOnlyList<Diagnostic> Diagnostics);
}
