using System.Text;
using System.Text.Json;
using Lunate.Ai;
using Lunate.Protocols;

namespace Lunate.Coding;

/// <summary>
/// Replaces the credential values Lunate itself holds with <c>[redacted:&lt;name&gt;]</c> before a
/// session leaves the machine (design decision 6). Replacement is by exact value only: no pattern
/// that "looks like a key" is touched, because a guess produces both false negatives and false
/// positives and teaches the reader to trust a filter that cannot be trusted. What Lunate knows
/// comes from the auth store and the provider environment variables, the extension secret files and
/// the MCP server environment values of the session's config files.
/// </summary>
internal sealed class CredentialRedactor
{
    /// <summary>
    /// Values shorter than this are skipped: replacing a two-character secret would rewrite the
    /// document without protecting anything a reader could use.
    /// </summary>
    internal const int MinimumValueLength = 4;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IReadOnlyList<(string Name, string Value)> _values;

    /// <summary>A redactor that knows nothing; the export still writes the review notice.</summary>
    internal static CredentialRedactor Empty { get; } = new([]);

    internal CredentialRedactor(IEnumerable<(string Name, string Value)> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values =
        [
            .. values
                .Where(entry =>
                    !string.IsNullOrWhiteSpace(entry.Value)
                    && entry.Value.Length >= MinimumValueLength
                )
                .Distinct()
                // Longest first: a shorter value that is a prefix of a longer one must not leave
                // the rest of the longer secret in place.
                .OrderByDescending(entry => entry.Value.Length),
        ];
    }

    /// <summary>How many distinct values the redactor will replace.</summary>
    internal int Count => _values.Count;

    /// <summary>The exact value/name pairs, longest value first (the tests read this).</summary>
    internal IReadOnlyList<(string Name, string Value)> Values => _values;

    /// <summary>The text with every known credential value replaced by its marker.</summary>
    internal string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string redacted = text;
        foreach ((string name, string value) in _values)
        {
            if (redacted.Contains(value, StringComparison.Ordinal))
            {
                redacted = redacted.Replace(value, Marker(name), StringComparison.Ordinal);
            }
        }

        return redacted;
    }

    /// <summary>
    /// The byte-level form, for the JSONL export: the session file is edited in place byte for
    /// byte, so a line without a credential stays exactly the bytes it was.
    /// </summary>
    internal byte[] RedactUtf8(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        byte[] redacted = source;
        foreach ((string name, string value) in _values)
        {
            byte[] pattern = Utf8NoBom.GetBytes(value);
            if (redacted.AsSpan().IndexOf(pattern) < 0)
            {
                continue;
            }

            redacted = ReplaceBytes(redacted, pattern, Utf8NoBom.GetBytes(Marker(name)));
        }

        return redacted;
    }

    /// <summary>The marker the session's export writes in place of a known value.</summary>
    internal static string Marker(string name) => "[redacted:" + name + "]";

    /// <summary>
    /// Collects every value the session can know: the auth store's keys, each provider preset's
    /// environment variable and alias, every extension secret file and every MCP server's
    /// environment values (the expanded values, which are the ones a server receives).
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

    private static byte[] ReplaceBytes(byte[] source, byte[] pattern, byte[] replacement)
    {
        var builder = new List<byte>(source.Length);
        var index = 0;
        while (index < source.Length)
        {
            int match = source.AsSpan(index).IndexOf(pattern);
            if (match < 0)
            {
                builder.AddRange(source.AsSpan(index).ToArray());
                break;
            }

            builder.AddRange(source.AsSpan(index, match).ToArray());
            builder.AddRange(replacement);
            index += match + pattern.Length;
        }

        return [.. builder];
    }
}
