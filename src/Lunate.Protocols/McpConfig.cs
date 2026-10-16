using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunate.Protocols;

/// <summary>
/// The load result of one config file. <see cref="RawServers"/> are the definitions exactly as
/// written — the input of trust hashes; <see cref="Servers"/> are the same servers after
/// <c>${VAR}</c> expansion — the definitions that run. <see cref="Warnings"/> names each server
/// whose definition referenced an unset variable with no default.
/// </summary>
internal sealed record McpConfigResult(
    IReadOnlyList<McpServerOptions> Servers,
    IReadOnlyList<McpServerOptions> RawServers,
    IReadOnlyList<string> Errors,
    IReadOnlyList<McpServerWarning> Warnings
)
{
    internal static McpConfigResult Empty { get; } = new([], [], [], []);
}

/// <summary>
/// The <c>mcp.json</c> parser: the Claude-Desktop-compatible <c>mcpServers</c> map keyed by server
/// name. <c>command</c> is required; <c>args</c>, <c>env</c>, <c>cwd</c>,
/// <c>callTimeoutSeconds</c> and <c>workspaceIndependent</c> are optional; unknown fields are
/// ignored. A malformed entry yields an error naming the server and field and is skipped; a
/// malformed or unreadable file yields one error carrying the path. A missing file is zero servers
/// and zero errors. <c>command</c>, <c>args</c> and <c>env</c> values are expanded at load
/// (Claude Code-compatible <c>${VAR}</c> / <c>${VAR:-default}</c>; see
/// <see cref="McpConfigExpansion"/>), while the raw definitions stay available for trust hashing.
/// The file is read by the caller at session start only.
/// </summary>
internal static class McpConfig
{
    internal static McpConfigResult Load(string path) => Load(path, environment: null);

    /// <summary>
    /// Loads with an explicit variable resolver; null reads the process environment.
    /// </summary>
    internal static McpConfigResult Load(string path, Func<string, string?>? environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return McpConfigResult.Empty;
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Error($"MCP config file '{path}' could not be read: {exception.Message}");
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            return Error($"MCP config file '{path}' is not valid JSON: {Position(exception)}");
        }

        if (root is not JsonObject document)
        {
            return Error($"MCP config file '{path}' must contain a JSON object.");
        }

        if (document["mcpServers"] is null)
        {
            return McpConfigResult.Empty;
        }

        if (document["mcpServers"] is not JsonObject servers)
        {
            return Error(
                $"MCP config file '{path}': 'mcpServers' must be an object mapping server names to server definitions."
            );
        }

        List<McpServerOptions> raw = [];
        List<string> errors = [];
        foreach ((string name, JsonNode? entry) in servers)
        {
            if (entry is not JsonObject server)
            {
                errors.Add(
                    $"MCP config file '{path}': server '{name}' must be a JSON object; the server is skipped."
                );
                continue;
            }

            if (TryRead(name, server, path, errors) is { } option)
            {
                raw.Add(option);
            }
        }

        List<McpServerWarning> warnings = [];
        List<McpServerOptions> expanded = [];
        foreach (McpServerOptions server in raw)
        {
            expanded.Add(McpConfigExpansion.Expand(server, environment, warnings));
        }

        return new McpConfigResult(expanded, raw, errors, warnings);
    }

    private static McpConfigResult Error(string message) => new([], [], [message], []);

    private static McpServerOptions? TryRead(
        string name,
        JsonObject server,
        string path,
        List<string> errors
    )
    {
        string? command = ReadString(server, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            errors.Add(
                $"MCP config file '{path}': server '{name}': 'command' must be a non-empty string; the server is skipped."
            );
            return null;
        }

        IReadOnlyList<string>? arguments = null;
        if (server.ContainsKey("args"))
        {
            if (server["args"] is not JsonArray array)
            {
                return Malformed(name, "args", "an array of strings", path, errors);
            }

            List<string> values = [];
            foreach (JsonNode? item in array)
            {
                if (item is not JsonValue value || !value.TryGetValue<string>(out string? text))
                {
                    return Malformed(name, "args", "an array of strings", path, errors);
                }

                values.Add(text);
            }

            arguments = values;
        }

        IReadOnlyDictionary<string, string>? environment = null;
        if (server.ContainsKey("env"))
        {
            if (server["env"] is not JsonObject env)
            {
                return Malformed(name, "env", "an object mapping names to strings", path, errors);
            }

            Dictionary<string, string> values = new(StringComparer.Ordinal);
            foreach ((string key, JsonNode? item) in env)
            {
                if (item is not JsonValue value || !value.TryGetValue<string>(out string? text))
                {
                    return Malformed(
                        name,
                        "env",
                        "an object mapping names to strings",
                        path,
                        errors
                    );
                }

                values[key] = text;
            }

            environment = values;
        }

        string? workingDirectory = null;
        if (server.ContainsKey("cwd"))
        {
            workingDirectory = ReadString(server, "cwd");
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                return Malformed(name, "cwd", "a non-empty string", path, errors);
            }
        }

        TimeSpan? callTimeout = null;
        if (server.ContainsKey("callTimeoutSeconds"))
        {
            if (
                server["callTimeoutSeconds"] is not JsonValue value
                || !value.TryGetValue<double>(out double seconds)
                || !double.IsFinite(seconds)
                || seconds <= 0
            )
            {
                return Malformed(name, "callTimeoutSeconds", "a positive number", path, errors);
            }

            callTimeout = TimeSpan.FromSeconds(seconds);
        }

        bool workspaceIndependent = false;
        if (server.ContainsKey("workspaceIndependent"))
        {
            if (
                server["workspaceIndependent"] is not JsonValue value
                || !value.TryGetValue<bool>(out bool parsed)
            )
            {
                return Malformed(name, "workspaceIndependent", "a boolean", path, errors);
            }

            workspaceIndependent = parsed;
        }

        return new McpServerOptions(
            name,
            command,
            arguments ?? [],
            environment,
            workingDirectory,
            callTimeout,
            NamespaceName: name,
            Source: path,
            WorkspaceIndependent: workspaceIndependent
        );
    }

    private static string? ReadString(JsonObject server, string field) =>
        server[field] is JsonValue value && value.TryGetValue<string>(out string? text)
            ? text
            : null;

    private static McpServerOptions? Malformed(
        string name,
        string field,
        string expected,
        string path,
        List<string> errors
    )
    {
        errors.Add(
            $"MCP config file '{path}': server '{name}': '{field}' must be {expected}; the server is skipped."
        );
        return null;
    }

    private static string Position(JsonException exception) =>
        exception.LineNumber is { } line
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{exception.Message} (line {line + 1}, position {exception.BytePositionInLine + 1})"
            )
            : exception.Message;
}
