namespace Lunate.Ai;

/// <summary>
/// A named user-owned provider access entry. Credential and header references point to secret
/// sources; they never contain resolved values. <see cref="BaseUrls"/> may key a URL per API
/// family (families may share a URL); <see cref="BaseUrl"/> stays accepted and means the kind's
/// default family (<c>openai-chat</c> for OpenAI-compatible kinds, <c>anthropic-messages</c> for
/// <c>anthropic</c>). Reading never rewrites a user file.
/// </summary>
public sealed record ProviderEntry(
    string Name,
    string Kind,
    Uri BaseUrl,
    string CredentialReference,
    IReadOnlyDictionary<string, string>? Headers = null,
    string? Region = null,
    IReadOnlyDictionary<ProviderApiFamily, Uri>? BaseUrls = null,
    Uri? ModelsUrl = null,
    IReadOnlyDictionary<ProviderApiFamily, CredentialPlacement>? CredentialPlacements = null
)
{
    /// <summary>
    /// The family <see cref="BaseUrl"/> shorthand names when <see cref="BaseUrls"/> is absent:
    /// <see cref="ProviderApiFamily.AnthropicMessages"/> for the <c>anthropic</c> kind,
    /// <see cref="ProviderApiFamily.OpenAiChat"/> for everything else.
    /// </summary>
    public ProviderApiFamily DefaultFamily =>
        ProviderPresets.Find(Kind)?.DefaultFamily ?? ProviderApiFamily.OpenAiChat;

    /// <summary>
    /// The families this entry serves: the declared <see cref="BaseUrls"/> keys (in stable
    /// declaration order) when present, otherwise just <see cref="DefaultFamily"/>.
    /// </summary>
    public IReadOnlyList<ProviderApiFamily> Families =>
        BaseUrls is { Count: > 0 }
            ? [.. ProviderApiFamilies.All.Where(BaseUrls.ContainsKey)]
            : [DefaultFamily];

    /// <summary>
    /// The URL a family routes to, or null when the entry does not declare it. Without
    /// <see cref="BaseUrls"/> only <see cref="DefaultFamily"/> resolves, to <see cref="BaseUrl"/>.
    /// </summary>
    public Uri? UrlFor(ProviderApiFamily family) =>
        BaseUrls is { Count: > 0 } ? BaseUrls.GetValueOrDefault(family)
        : family == DefaultFamily ? BaseUrl
        : null;

    /// <summary>
    /// The credential placement a family's wire uses: the entry's own declaration, or the kind
    /// preset's, or the default <see cref="CredentialPlacement.Bearer"/>. An entry's declaration
    /// wins over the preset's, because the entry is the user's explicit intent.
    /// </summary>
    public CredentialPlacement PlacementFor(ProviderApiFamily family)
    {
        if (CredentialPlacements?.TryGetValue(family, out CredentialPlacement? declared) == true)
        {
            return declared;
        }

        return ProviderPresets.Find(Kind)?.PlacementFor(family) ?? CredentialPlacement.Bearer;
    }

    internal static bool TryCreate(
        string? name,
        string? kind,
        Uri? baseUrl,
        string? credentialReference,
        IReadOnlyDictionary<string, string>? headers,
        string? region,
        out ProviderEntry? entry,
        out string? error
    ) =>
        TryCreate(
            name,
            kind,
            baseUrl,
            credentialReference,
            headers,
            region,
            baseUrls: null,
            modelsUrl: null,
            credentialPlacements: null,
            out entry,
            out error
        );

    internal static bool TryCreate(
        string? name,
        string? kind,
        Uri? baseUrl,
        string? credentialReference,
        IReadOnlyDictionary<string, string>? headers,
        string? region,
        IReadOnlyDictionary<ProviderApiFamily, Uri>? baseUrls,
        Uri? modelsUrl,
        out ProviderEntry? entry,
        out string? error
    ) =>
        TryCreate(
            name,
            kind,
            baseUrl,
            credentialReference,
            headers,
            region,
            baseUrls,
            modelsUrl,
            credentialPlacements: null,
            out entry,
            out error
        );

    internal static bool TryCreate(
        string? name,
        string? kind,
        Uri? baseUrl,
        string? credentialReference,
        IReadOnlyDictionary<string, string>? headers,
        string? region,
        IReadOnlyDictionary<ProviderApiFamily, Uri>? baseUrls,
        Uri? modelsUrl,
        IReadOnlyDictionary<ProviderApiFamily, CredentialPlacement>? credentialPlacements,
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

        if (baseUrl is not null && (!baseUrl.IsAbsoluteUri || !IsAllowedEndpoint(baseUrl)))
        {
            error = $"providers.{name}.baseUrl must be https or loopback http";
            return false;
        }

        Dictionary<ProviderApiFamily, Uri>? copiedFamilyUrls = null;
        if (baseUrls is { Count: > 0 })
        {
            copiedFamilyUrls = [];
            foreach ((ProviderApiFamily family, Uri familyUrl) in baseUrls)
            {
                if (!familyUrl.IsAbsoluteUri || !IsAllowedEndpoint(familyUrl))
                {
                    error =
                        $"providers.{name}.baseUrls.{ProviderApiFamilies.Name(family)} must be https or loopback http";
                    return false;
                }

                copiedFamilyUrls.Add(family, familyUrl);
            }

            if (baseUrl is null)
            {
                // The file declared only baseUrls: the shorthand baseUrl is the kind's default
                // family URL, so derived from it rather than refused.
                baseUrl = copiedFamilyUrls.GetValueOrDefault(preset.DefaultFamily);
                if (baseUrl is null)
                {
                    error =
                        $"providers.{name}.baseUrls must declare {ProviderApiFamilies.Name(preset.DefaultFamily)} when 'baseUrl' is absent";
                    return false;
                }
            }
        }

        if (baseUrl is null)
        {
            error = $"providers.{name} must declare 'baseUrl' or 'baseUrls'";
            return false;
        }

        if (modelsUrl is not null && (!modelsUrl.IsAbsoluteUri || !IsAllowedEndpoint(modelsUrl)))
        {
            error = $"providers.{name}.modelsUrl must be https or loopback http";
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
                if (!ProviderHeaderValues.TryValidate(header, reference, out string? headerError))
                {
                    error = $"providers.{name}.headers.{header} {headerError}";
                    return false;
                }

                copiedHeaders.Add(header, reference);
            }
        }

        Dictionary<ProviderApiFamily, CredentialPlacement>? copiedPlacements = null;
        if (credentialPlacements is { Count: > 0 })
        {
            copiedPlacements = new(credentialPlacements);
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
            region,
            copiedFamilyUrls,
            modelsUrl,
            copiedPlacements
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
