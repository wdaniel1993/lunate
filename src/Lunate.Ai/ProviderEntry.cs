namespace Lunate.Ai;

/// <summary>
/// A named user-owned provider access entry. Credential and header references point to secret
/// sources; they never contain resolved values.
/// </summary>
public sealed record ProviderEntry(
    string Name,
    string Kind,
    Uri BaseUrl,
    string CredentialReference,
    IReadOnlyDictionary<string, string>? Headers = null,
    string? Region = null
)
{
    internal static bool TryCreate(
        string? name,
        string? kind,
        Uri? baseUrl,
        string? credentialReference,
        IReadOnlyDictionary<string, string>? headers,
        string? region,
        out ProviderEntry? entry,
        out string? error
    )
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsWhiteSpace) || name.Contains('/'))
        {
            error = "name must be non-empty and contain no whitespace or '/'";
            return false;
        }

        ProviderPreset? preset = kind is null ? null : ProviderPresets.Find(kind);
        if (preset is null)
        {
            error = $"providers.{name}.kind must name one of the twelve provider templates";
            return false;
        }

        if (baseUrl is null || !baseUrl.IsAbsoluteUri || !IsAllowedEndpoint(baseUrl))
        {
            error = $"providers.{name}.baseUrl must be https or loopback http";
            return false;
        }

        if (!IsCredentialReference(credentialReference))
        {
            error =
                $"providers.{name}.credentialReference must be an auth:, env:, or helper: reference";
            return false;
        }

        Dictionary<string, string>? copiedHeaders = null;
        if (headers is not null)
        {
            copiedHeaders = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string header, string reference) in headers)
            {
                if (
                    string.IsNullOrWhiteSpace(header)
                    || header.Any(char.IsControl)
                    || header.Contains(':')
                )
                {
                    error = $"providers.{name}.headers contains an invalid header name";
                    return false;
                }

                if (!IsCredentialReference(reference))
                {
                    error =
                        $"providers.{name}.headers.{header} must be an auth:, env:, or helper: reference";
                    return false;
                }

                copiedHeaders.Add(header, reference);
            }
        }

        if (region is not null && region is not ("eu" or "us" or "local"))
        {
            error = $"providers.{name}.region must be 'eu', 'us', or 'local'";
            return false;
        }

        entry = new ProviderEntry(
            name,
            preset.Id,
            baseUrl,
            credentialReference!,
            copiedHeaders,
            region
        );
        error = null;
        return true;
    }

    internal static bool IsCredentialReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        int separator = reference.IndexOf(':');
        if (separator <= 0 || separator == reference.Length - 1)
        {
            return false;
        }

        string source = reference[..separator];
        string target = reference[(separator + 1)..];
        return source switch
        {
            "auth" or "helper" => !target.Any(char.IsWhiteSpace) && !target.Contains('/'),
            "env" => IsEnvironmentVariable(target),
            _ => false,
        };
    }

    private static bool IsAllowedEndpoint(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        || (
            uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && uri.IsLoopback
        );

    private static bool IsEnvironmentVariable(string value) =>
        (char.IsLetter(value[0]) || value[0] == '_')
        && value.All(character => char.IsLetterOrDigit(character) || character == '_');
}
