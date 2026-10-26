using System.Text.Json;
using Lunate.Ai;
using Lunate.Protocols;

namespace Lunate.Coding;

/// <summary>
/// Collects the credential values a session can know and hands them to the one redactor
/// (<see cref="CredentialRedactor"/>, which lives in <c>Lunate.Ai</c> so the agent loop can redact
/// what it persists without referencing this assembly). Collection needs the auth store, the
/// provider presets and the MCP config files, which is why it stays here; the replacement itself is
/// never duplicated.
/// </summary>
internal static class CredentialRedactorSource
{
    /// <summary>
    /// Every value the session can know: the auth store's keys, each provider preset's environment
    /// variable and alias, every extension secret file and every MCP server's environment values
    /// (the expanded values, which are the ones a server receives).
    /// </summary>
    internal static CredentialRedactor Collect(
        AuthStore auth,
        Func<string, string?> environment,
        string storePath,
        IReadOnlyList<string> mcpConfigPaths
    )
    {
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);
        ArgumentNullException.ThrowIfNull(mcpConfigPaths);

        List<(string Name, string Value)> values = [];
        foreach ((string name, string secret) in auth.Keys)
        {
            values.Add((name, secret));
        }

        foreach (ProviderPreset preset in ProviderPresets.All)
        {
            AddVariable(values, environment, preset.KeyVariable);
            if (preset.KeyVariableAlias is { } alias)
            {
                AddVariable(values, environment, alias);
            }
        }

        values.AddRange(ExtensionSecrets(storePath));
        values.AddRange(McpEnvironment(mcpConfigPaths, environment));
        return new CredentialRedactor(values);
    }

    private static void AddVariable(
        List<(string Name, string Value)> values,
        Func<string, string?> environment,
        string variable
    )
    {
        if (environment(variable) is { Length: > 0 } value)
        {
            values.Add((variable, value));
        }
    }

    /// <summary>
    /// Every string in <c>&lt;store&gt;/extensions-secrets/&lt;id&gt;.json</c>, named by extension and
    /// key. An unreadable or malformed file contributes nothing: redaction is best-effort and must
    /// never fail the export.
    /// </summary>
    private static IEnumerable<(string Name, string Value)> ExtensionSecrets(string storePath)
    {
        string directory = Path.Combine(storePath, "extensions-secrets");
        string[] files;
        try
        {
            files = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json") : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            string extensionId = Path.GetFileNameWithoutExtension(file);
            Dictionary<string, string>? secrets = ReadFlatObject(file);
            if (secrets is null)
            {
                continue;
            }

            foreach ((string key, string value) in secrets)
            {
                yield return (extensionId + "." + key, value);
            }
        }
    }

    /// <summary>
    /// Each configured server's environment values, named <c>mcp.&lt;server&gt;.&lt;variable&gt;</c>.
    /// The definitions are read exactly as the session reads them, so <c>${VAR}</c> is expanded
    /// before it is collected; a file that does not load contributes nothing.
    /// </summary>
    private static IEnumerable<(string Name, string Value)> McpEnvironment(
        IReadOnlyList<string> configPaths,
        Func<string, string?> environment
    )
    {
        foreach (string path in configPaths)
        {
            List<McpServerOptions> servers;
            try
            {
                servers = [.. McpConfig.Load(path, environment).Servers];
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                continue;
            }

            foreach (McpServerOptions server in servers)
            {
                if (server.Environment is not { } values)
                {
                    continue;
                }

                foreach ((string name, string value) in values)
                {
                    yield return ("mcp." + server.Name + "." + name, value);
                }
            }
        }
    }

    private static Dictionary<string, string>? ReadFlatObject(string path)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            Dictionary<string, string> values = new(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (
                    property.Value.ValueKind == JsonValueKind.String
                    && property.Value.GetString() is { Length: > 0 } value
                )
                {
                    values[property.Name] = value;
                }
            }

            return values;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
