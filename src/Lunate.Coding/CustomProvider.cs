using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>
/// A user-declared OpenAI-compatible endpoint, stored as a <c>providers</c> settings entry. The
/// key lives only in <c>auth.json</c> under <see cref="Id"/>; <see cref="CatalogName"/> is the
/// model id the catalog exposes (default <c>&lt;id&gt;/&lt;model&gt;</c>).
/// </summary>
public sealed record CustomProvider(string Id, Uri BaseUrl, string Model, string CatalogName)
{
    /// <summary>
    /// Validates and trims the entered fields. The URL must be absolute and <c>https</c>, or
    /// <c>http</c> only for a loopback host; the provider id must not contain spaces or a slash
    /// (it prefixes the catalog name) and must not shadow a built-in preset. Error messages never
    /// echo an entered value except the refused scheme.
    /// </summary>
    internal static bool TryCreate(
        string? id,
        string? baseUrl,
        string? model,
        string? catalogName,
        out CustomProvider? provider,
        out string? error
    )
    {
        provider = null;
        string? trimmedId = id?.Trim();
        if (string.IsNullOrEmpty(trimmedId))
        {
            error = "the provider id is required";
            return false;
        }

        if (trimmedId.Any(char.IsWhiteSpace) || trimmedId.Contains('/'))
        {
            error = "the provider id must not contain spaces or '/'";
            return false;
        }

        if (ProviderPresets.Find(trimmedId) is not null)
        {
            error = "the provider id is a built-in provider; choose a different id";
            return false;
        }

        string? trimmedBaseUrl = baseUrl?.Trim();
        if (string.IsNullOrEmpty(trimmedBaseUrl))
        {
            error = "the base URL is required";
            return false;
        }

        if (!Uri.TryCreate(trimmedBaseUrl, UriKind.Absolute, out Uri? uri))
        {
            error = "the base URL must be an absolute URL";
            return false;
        }

        bool isHttps = string.Equals(
            uri.Scheme,
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase
        );
        bool isLoopbackHttp =
            string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && uri.IsLoopback;
        if (!isHttps && !isLoopbackHttp)
        {
            error =
                $"the base URL scheme '{uri.Scheme}' is not supported; use https, or http for a loopback host";
            return false;
        }

        string? trimmedModel = model?.Trim();
        if (string.IsNullOrEmpty(trimmedModel))
        {
            error = "the model id is required";
            return false;
        }

        string resolvedCatalogName = catalogName?.Trim() is { Length: > 0 } explicitName
            ? explicitName
            : $"{trimmedId}/{trimmedModel}";
        provider = new CustomProvider(trimmedId, uri, trimmedModel, resolvedCatalogName);
        error = null;
        return true;
    }

    /// <summary>
    /// The catalog entry the stored provider contributes. It routes through the native
    /// <c>openai</c> preset with this provider's endpoint, and names its auth-store key, which is
    /// the explicit opt-in that lets that key reach the declared endpoint.
    /// </summary>
    internal ModelInfo ToModelInfo() =>
        new(CatalogName, "openai", BaseUrl, ContextWindow: 0, SupportsTools: true, AuthRef: Id)
        {
            // The catalog id is what /model shows; the upstream endpoint receives the model id.
            WireId = Model,
        };
}
