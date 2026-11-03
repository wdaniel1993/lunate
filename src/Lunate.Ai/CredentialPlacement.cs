namespace Lunate.Ai;

/// <summary>
/// How one provider entry or preset wants the credential on the wire:
/// <c>Authorization: Bearer</c>-style (the default), another named header, or a query parameter.
/// The placement is declared data; the credential value never is. Renderer and carrier both live
/// in the credential pipeline; settings files declare it as a compact string (<c>bearer</c>,
/// <c>x-api-key</c>, <c>query:key</c>) or as an object
/// (<c>{ "in": "header", "name": "x-api-key" }</c>), and a provider may key one per family so its
/// OpenAI-compatible surface keeps the bearer its endpoint documents while the native one takes
/// its own form (Google).
/// </summary>
public enum CredentialPlacementTarget
{
    Header,
    Query,
}

public sealed record CredentialPlacement(
    string Name,
    CredentialPlacementTarget Target = CredentialPlacementTarget.Header,
    string Scheme = ""
)
{
    internal static readonly CredentialPlacement Bearer = new("Authorization", Scheme: "Bearer ");

    internal static readonly CredentialPlacement XApiKey = new("x-api-key");

    /// <summary>The header value: an empty scheme sends the credential alone.</summary>
    internal string HeaderValue(string credential) => Scheme + credential;

    /// <summary>
    /// Parses a compact declaration (<c>bearer</c>, <c>x-api-key</c>, <c>header:&lt;name&gt;</c>,
    /// <c>query:&lt;name&gt;</c>). Anything else is refused naming the value, because an unknown
    /// compact form today is a typo the wire would answer with an auth failure only.
    /// </summary>
    internal static bool TryParseCompact(
        string? declared,
        out CredentialPlacement? placement,
        out string? error
    )
    {
        placement = null;
        error = null;
        switch (declared)
        {
            case null or "":
                error = "the placement name is empty";
                return false;
            case "bearer":
                placement = Bearer;
                return true;
            case "x-api-key":
                placement = XApiKey;
                return true;
        }

        if (declared.StartsWith("query:", StringComparison.Ordinal))
        {
            if (
                !TryCreate(
                    declared["query:".Length..],
                    CredentialPlacementTarget.Query,
                    null,
                    out placement,
                    out error
                )
            )
            {
                error = $"'{declared}' is not a usable placement: {error}";
                return false;
            }

            return true;
        }

        if (declared.StartsWith("header:", StringComparison.Ordinal))
        {
            if (
                !TryCreate(
                    declared["header:".Length..],
                    CredentialPlacementTarget.Header,
                    null,
                    out placement,
                    out error
                )
            )
            {
                error = $"'{declared}' is not a usable placement: {error}";
                return false;
            }

            return true;
        }

        error =
            $"'{declared}' is not a placement: declare 'bearer', 'x-api-key', 'header:<name>' or 'query:<name>'.";
        return false;
    }

    /// <summary>
    /// Validates one placement: an RFC 7230 token for a header name, a URL-safe name for a query
    /// parameter, and a scheme that is printable ASCII without line breaks (a missing trailing
    /// space is added, so <c>"Bearer"</c> means <c>"Bearer "</c>).
    /// </summary>
    internal static bool TryCreate(
        string? name,
        CredentialPlacementTarget target,
        string? scheme,
        out CredentialPlacement? placement,
        out string? error
    )
    {
        placement = null;
        error = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "the placement name is empty";
            return false;
        }

        if (name.Any(char.IsWhiteSpace) || name.Contains(':') || name.Contains('/'))
        {
            error = $"'{name}' is not a valid name (no whitespace, ':' or '/')";
            return false;
        }

        if (target == CredentialPlacementTarget.Header && !IsValidHeaderName(name))
        {
            error = $"'{name}' is not a valid header name";
            return false;
        }

        string? normalizedScheme = NormalizeScheme(scheme, out string? schemeError);
        if (schemeError is not null)
        {
            error = schemeError;
            return false;
        }

        placement = new CredentialPlacement(name, target, normalizedScheme!);
        return true;
    }

    /// <summary>A scheme is printable ASCII without line breaks and ends in one space when set.</summary>
    private static string? NormalizeScheme(string? scheme, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(scheme))
        {
            return "";
        }

        if (scheme.Any(character => character is < ' ' or > '~'))
        {
            error = $"the scheme '{scheme}' is not printable ASCII without control characters";
            return null;
        }

        return scheme.EndsWith(' ') ? scheme : scheme + " ";
    }

    /// <summary>The compact form the model and the auth wizard round-trip; null when Bearer.</summary>
    internal string? ToCompact() =>
        this == Bearer ? "bearer"
        : this == XApiKey ? "x-api-key"
        : Target == CredentialPlacementTarget.Query ? "query:" + Name
        : "header:" + Name;

    internal static bool IsValidHeaderName(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        foreach (char character in name)
        {
            if (
                !char.IsAsciiLetterOrDigit(character)
                && character
                    is not (
                        '!'
                        or '#'
                        or '$'
                        or '%'
                        or '&'
                        or '\''
                        or '*'
                        or '+'
                        or '-'
                        or '.'
                        or '^'
                        or '_'
                        or '`'
                        or '|'
                        or '~'
                    )
            )
            {
                return false;
            }
        }

        return true;
    }
}
