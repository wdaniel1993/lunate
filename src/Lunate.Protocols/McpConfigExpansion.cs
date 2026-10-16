using System.Text;

namespace Lunate.Protocols;

/// <summary>
/// One expansion warning: the server's definition referenced an unset environment variable with no
/// default, so the literal <c>${VAR}</c> text is used. Surfaced as a session notice and next to
/// the server in <c>/mcp</c>.
/// </summary>
internal sealed record McpServerWarning(string Server, string Variable)
{
    internal string Message =>
        FormattableString.Invariant(
            $"MCP server '{Server}' references unset environment variable '{Variable}' with no default; the literal '${{{Variable}}}' text is used."
        );
}

/// <summary>
/// The Claude Code-compatible expansion of <c>${VAR}</c> and <c>${VAR:-default}</c> in a server
/// definition's <c>command</c>, <c>args</c> and <c>env</c> values (never env keys). A variable
/// that is unset with no default keeps the literal text and records one warning per server and
/// variable; a set (even empty) variable and a matching default substitute. Text that does not
/// match the pattern — <c>$VAR</c>, <c>${}</c>, <c>${1BAD}</c>, an unclosed <c>${</c> — stays
/// literal. Expansion produces the expanded definitions; the raw definitions remain the parser's
/// other half of the load result, which is what trust hashes see.
/// </summary>
internal static class McpConfigExpansion
{
    internal static McpServerOptions Expand(
        McpServerOptions server,
        Func<string, string?>? environment,
        List<McpServerWarning> warnings
    )
    {
        Func<string, string?> lookup = environment ?? System.Environment.GetEnvironmentVariable;
        IReadOnlyDictionary<string, string>? values = null;
        if (server.Environment is not null)
        {
            Dictionary<string, string> expanded = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> entry in server.Environment)
            {
                expanded[entry.Key] = ExpandText(entry.Value, server.Name, lookup, warnings);
            }

            values = expanded;
        }

        return server with
        {
            Command = ExpandText(server.Command, server.Name, lookup, warnings),
            Arguments =
            [
                .. server.Arguments.Select(argument =>
                    ExpandText(argument, server.Name, lookup, warnings)
                ),
            ],
            Environment = values,
        };
    }

    private static string ExpandText(
        string text,
        string server,
        Func<string, string?> environment,
        List<McpServerWarning> warnings
    )
    {
        if (!text.Contains("${", StringComparison.Ordinal))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            int start = text.IndexOf("${", index, StringComparison.Ordinal);
            if (start < 0)
            {
                builder.Append(text, index, text.Length - index);
                break;
            }

            builder.Append(text, index, start - index);
            int end = text.IndexOf('}', start + 2);
            if (end < 0)
            {
                builder.Append(text, start, text.Length - start);
                break;
            }

            string body = text[(start + 2)..end];
            if (TryRead(body, out string name, out string? fallback))
            {
                string? value = environment(name);
                if (value is not null)
                {
                    builder.Append(value);
                }
                else if (fallback is not null)
                {
                    builder.Append(fallback);
                }
                else
                {
                    builder.Append(text, start, end + 1 - start);
                    AddWarning(warnings, server, name);
                }
            }
            else
            {
                builder.Append(text, start, end + 1 - start);
            }

            index = end + 1;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Splits one reference body: a variable name, or a name plus default after the first
    /// <c>:-</c> (the default may be empty and is used literally, never expanded).
    /// </summary>
    private static bool TryRead(string body, out string name, out string? fallback)
    {
        int separator = body.IndexOf(":-", StringComparison.Ordinal);
        name = separator < 0 ? body : body[..separator];
        fallback = separator < 0 ? null : body[(separator + 2)..];
        return IsName(name);
    }

    private static bool IsName(string name)
    {
        if (name.Length == 0 || (name[0] != '_' && !char.IsAsciiLetter(name[0])))
        {
            return false;
        }

        return name.All(character => character == '_' || char.IsAsciiLetterOrDigit(character));
    }

    private static void AddWarning(List<McpServerWarning> warnings, string server, string variable)
    {
        if (
            warnings.Any(warning =>
                string.Equals(warning.Server, server, StringComparison.Ordinal)
                && string.Equals(warning.Variable, variable, StringComparison.Ordinal)
            )
        )
        {
            return;
        }

        warnings.Add(new McpServerWarning(server, variable));
    }
}
