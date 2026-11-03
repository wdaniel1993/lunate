using System.Globalization;
using System.Text;

namespace Lunate.Ai;

/// <summary>
/// The header value model a provider entry or preset declares: a value is either a credential
/// reference (<c>auth:</c>, <c>env:</c>, <c>helper:</c>), or a literal/template over the fixed
/// variable set <c>{session.id}</c>, <c>{run.id}</c>, <c>{lunate.version}</c>. Names, values and
/// placeholders are validated at load; a value containing <c>{</c> that is not one of the three
/// placeholders — including the forms that would carry the credential
/// (<c>{credential}</c>, <c>{apiKey}</c>, <c>{key}</c>) — is refused by name. There is no
/// environment access inside a template: a reference form resolves at send time over the host's
/// key sources, a template over the run's context.
/// </summary>
internal static class ProviderHeaderValues
{
    internal const string SessionPlaceholder = "{session.id}";
    internal const string RunPlaceholder = "{run.id}";
    internal const string VersionPlaceholder = "{lunate.version}";

    /// <summary>The maximum byte length a header value may render to (RFC 7230 practice).</summary>
    private const int MaxValueLength = 8 * 1024;

    /// <summary>
    /// Placeholder names a header template must never carry: each would put the credential into
    /// a header template, which defeats the placement rule and would leak a secret into a
    /// template-derived header.
    /// </summary>
    private static readonly string[] CredentialPlaceholderNames = ["credential", "apiKey", "key"];

    internal static bool IsReference(string value)
    {
        int separator = value.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            return false;
        }

        string source = value[..separator];
        string target = value[(separator + 1)..];
        return source switch
        {
            "auth" or "helper" => !target.Any(char.IsWhiteSpace)
                && !target.Contains('/', StringComparison.Ordinal),
            "env" => target.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '_'
            ),
            _ => false,
        };
    }

    /// <summary>
    /// Validates one declared header. The name must be an RFC 7230 token; the value must be
    /// printable ASCII without line breaks and at most 8 KiB once rendered, and every
    /// <c>{</c>-sequence must be one of the three known placeholders.
    /// </summary>
    internal static bool TryValidate(string headerName, string value, out string? error)
    {
        error = null;
        if (!CredentialPlacement.IsValidHeaderName(headerName))
        {
            error = $"'{headerName}' is not a valid header name";
            return false;
        }

        if (value.Length == 0)
        {
            error = $"the value of header '{headerName}' is empty";
            return false;
        }

        if (value.Length > MaxValueLength)
        {
            error =
                $"the value of header '{headerName}' is longer than "
                + MaxValueLength.ToString(CultureInfo.InvariantCulture)
                + " bytes";
            return false;
        }

        if (value.Any(character => character < ' ' || character > '~'))
        {
            error =
                $"the value of header '{headerName}' is not printable ASCII without line breaks";
            return false;
        }

        error = UnusablePlaceholder(value);
        return error is null;
    }

    /// <summary>
    /// Renders a template at request time. A variable the effective context does not carry is an
    /// actionable error, not an empty header.
    /// </summary>
    internal static string RenderTemplate(
        string template,
        string headerName,
        RequestContext? context,
        Func<string, string?>? namedKeySource,
        Func<string, string?> environment
    )
    {
        if (IsReference(template))
        {
            return ResolveReference(template, headerName, namedKeySource, environment);
        }

        var substitutions = new List<(int Start, int Length, string Value)>();
        foreach ((int start, int end) in Placeholders(template))
        {
            // The span is exclusive at the end and includes the closing brace.
            substitutions.Add(
                (start, end - start, Value(template[start..end], headerName, context))
            );
        }

        if (substitutions.Count == 0)
        {
            return template;
        }

        int renderedLength =
            template.Length + substitutions.Sum(item => item.Value.Length - item.Length);
        var builder = new StringBuilder(renderedLength);
        int position = 0;
        foreach ((int start, int length, string substitution) in substitutions)
        {
            builder.Append(template, position, start - position);
            builder.Append(substitution);
            position = start + length;
        }

        builder.Append(template[position..]);
        return builder.ToString();
    }

    private static string Value(string token, string headerName, RequestContext? context) =>
        token switch
        {
            SessionPlaceholder => Require(
                context?.SessionId,
                headerName,
                SessionPlaceholder,
                "session id"
            ),
            RunPlaceholder => Require(context?.RunId, headerName, RunPlaceholder, "run id"),
            VersionPlaceholder => RequestContext.LunateVersion,
            _ => throw new InvalidOperationException(
                $"The value of header '{headerName}' declares an unknown placeholder '{token}'."
            ),
        };

    private static string Require(
        string? value,
        string headerName,
        string placeholder,
        string missing
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"The header '{headerName}' uses {placeholder}, but this request carries no {missing}. "
                    + "Send the request inside a session run, or declare the header without "
                    + $"{placeholder}."
            );
        }

        return value;
    }

    /// <summary>
    /// Resolves a reference-form header value at send time. An unresolvable auth or env reference
    /// is an actionable error; a helper reference is one this host cannot resolve — the session's
    /// credential sources are the auth store and the environment.
    /// </summary>
    internal static string ResolveReference(
        string reference,
        string headerName,
        Func<string, string?>? namedKeySource,
        Func<string, string?> environment
    )
    {
        int separator = reference.IndexOf(':', StringComparison.Ordinal);
        string source = reference[..separator];
        string target = reference[(separator + 1)..];
        if (source == "helper")
        {
            throw new InvalidOperationException(
                $"The header '{headerName}' declares the helper reference 'helper:{target}', which this "
                    + "host cannot resolve. Use an auth: or env: reference for the header instead."
            );
        }

        string? resolved = source == "auth" ? namedKeySource?.Invoke(target) : environment(target);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            string owner =
                source == "auth"
                    ? $"the auth-store key '{target}'"
                    : $"the environment variable {target}";
            throw new InvalidOperationException(
                $"The header '{headerName}' declares the reference '{source}:{target}', but {owner} is not "
                    + "set. Fix the header value or supply the value before using this provider."
            );
        }

        return resolved;
    }

    /// <summary>The first unusable <c>{</c>…<c>}</c> span of one template, or null when all is well.</summary>
    private static string? UnusablePlaceholder(string value)
    {
        if (IsReference(value))
        {
            return null;
        }

        int position = value.IndexOf('{', StringComparison.Ordinal);
        while (position >= 0)
        {
            int end = value.IndexOf('}', position + 1);
            if (end < 0)
            {
                return $"an unterminated '{{' at offset "
                    + position.ToString(CultureInfo.InvariantCulture);
            }

            string token = value[position..(end + 1)];
            if (!IsKnownPlaceholder(token))
            {
                return CredentialPlaceholderNames.Contains(token[1..^1])
                    ? token.TrimStart('{').TrimEnd('}')
                        + " names a credential: a header template cannot carry it, because the "
                        + "credential value lives in credentialPlacement only."
                    : $"'{token}' is not one of {SessionPlaceholder}, {RunPlaceholder}, {VersionPlaceholder}";
            }

            position = value.IndexOf('{', end + 1);
        }

        return null;
    }

    private static IEnumerable<(int Start, int End)> Placeholders(string value)
    {
        int position = value.IndexOf('{', StringComparison.Ordinal);
        while (position >= 0)
        {
            int end = value.IndexOf('}', position + 1);
            if (end < 0)
            {
                yield break;
            }

            // The span includes the closing brace: the substitution consumes the whole token.
            yield return (position, end + 1);
            position = value.IndexOf('{', end + 1);
        }
    }

    private static bool IsKnownPlaceholder(string token) =>
        token is SessionPlaceholder or RunPlaceholder or VersionPlaceholder;
}
