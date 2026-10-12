using System.Globalization;
using System.Text.Json.Nodes;

namespace FakeLspServer;

/// <summary>One server process: initialize handshake, document tracking and diagnostic publishing.</summary>
internal sealed class FakeServer(bool silent, bool staleFirst, int? crashAfter)
{
    private readonly Dictionary<string, int> _openVersions = new(StringComparer.Ordinal);
    private int _messages;

    public async Task RunAsync(Stream input, Stream output, CancellationToken ct = default)
    {
        while (true)
        {
            JsonNode? message = await JsonRpc.ReadAsync(input, ct);
            if (message is null)
            {
                return;
            }

            _messages++;
            if (await HandleAsync(message, output, ct))
            {
                return;
            }

            if (crashAfter is { } limit && _messages >= limit)
            {
                return;
            }
        }
    }

    /// <summary>Handles one message; true ends the process.</summary>
    private async Task<bool> HandleAsync(JsonNode message, Stream output, CancellationToken ct)
    {
        string method = message["method"]?.GetValue<string>() ?? string.Empty;
        if (message["id"] is { } id)
        {
            JsonObject response = method switch
            {
                "initialize" => Response(id, InitializeResult()),
                "shutdown" => Response(id, null),
                _ => ErrorResponse(id, -32601, $"unknown method '{method}'"),
            };
            await JsonRpc.WriteAsync(output, response, ct);
            return false;
        }

        switch (method)
        {
            case "exit":
                return true;
            case "initialized":
                return false;
            case "textDocument/didOpen":
            case "textDocument/didChange":
                await PublishAsync(message["params"], method, output, ct);
                return false;
            default:
                return false;
        }
    }

    private async Task PublishAsync(
        JsonNode? parameters,
        string method,
        Stream output,
        CancellationToken ct
    )
    {
        JsonNode? document = parameters?["textDocument"];
        string uri = document?["uri"]?.GetValue<string>() ?? string.Empty;
        int version = document?["version"]?.GetValue<int>() ?? 0;
        string text;
        if (method == "textDocument/didOpen")
        {
            text = document?["text"]?.GetValue<string>() ?? string.Empty;
            if (_openVersions.ContainsKey(uri))
            {
                await PublishAsync(output, uri, version, ProtocolDiagnostic(), ct);
                return;
            }

            _openVersions[uri] = version;
        }
        else
        {
            text = parameters?["contentChanges"]?[0]?["text"]?.GetValue<string>() ?? string.Empty;
        }

        if (silent)
        {
            return;
        }

        if (staleFirst)
        {
            await PublishAsync(output, uri, Math.Max(0, version - 1), StaleDiagnostics(), ct);
        }

        await PublishAsync(output, uri, version, Diagnose(text), ct);
    }

    private static async Task PublishAsync(
        Stream output,
        string uri,
        int version,
        IReadOnlyList<JsonObject> diagnostics,
        CancellationToken ct
    )
    {
        var parameters = new JsonObject
        {
            ["uri"] = uri,
            ["version"] = version,
            ["diagnostics"] = new JsonArray([.. diagnostics]),
        };
        var notification = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "textDocument/publishDiagnostics",
            ["params"] = parameters,
        };
        await JsonRpc.WriteAsync(output, notification, ct);
    }

    private static List<JsonObject> Diagnose(string text)
    {
        var diagnostics = new List<JsonObject>();
        string[] lines = text.Split('\n');
        for (int line = 0; line < lines.Length; line++)
        {
            string current = lines[line];
            if (current.Contains("FIXME", StringComparison.Ordinal))
            {
                diagnostics.Add(
                    Diagnostic(1, line, "fixme-check", $"FIXME left on line {Line(line)}")
                );
            }
            else if (current.Contains("TODO", StringComparison.Ordinal))
            {
                diagnostics.Add(
                    Diagnostic(2, line, "todo-check", $"TODO left on line {Line(line)}")
                );
            }
            else if (current.Contains("INFO", StringComparison.Ordinal))
            {
                diagnostics.Add(
                    Diagnostic(3, line, "info-check", $"information on line {Line(line)}")
                );
            }
            else if (current.Contains("HINT", StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic(4, line, "hint-check", $"hint on line {Line(line)}"));
            }
        }

        return diagnostics;
    }

    private static List<JsonObject> StaleDiagnostics() =>
        [Diagnostic(2, 0, "stale-check", "stale diagnostics from an older version")];

    private static List<JsonObject> ProtocolDiagnostic() =>
        [Diagnostic(1, 0, "duplicate-open", "duplicate didOpen for an open document")];

    private static JsonObject Diagnostic(int severity, int line, string code, string message) =>
        new()
        {
            ["severity"] = severity,
            ["code"] = code,
            ["message"] = message,
            ["range"] = new JsonObject
            {
                ["start"] = new JsonObject { ["line"] = line, ["character"] = 0 },
                ["end"] = new JsonObject { ["line"] = line, ["character"] = 1 },
            },
        };

    private static string Line(int zeroBasedLine) =>
        (zeroBasedLine + 1).ToString(CultureInfo.InvariantCulture);

    private static JsonObject InitializeResult() =>
        new() { ["capabilities"] = new JsonObject { ["textDocumentSync"] = 1 } };

    private static JsonObject Response(JsonNode id, JsonNode? result) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result,
        };

    private static JsonObject ErrorResponse(JsonNode id, int code, string message) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        };
}
