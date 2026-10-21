namespace Lunate.Ai;

/// <summary>
/// Whether a declared endpoint belongs to a provider preset's own base URL: the same scheme, host
/// and port, with a path at or under the preset's base path. Scheme and host compare
/// case-insensitively and trailing slashes are ignored; a path prefix only counts at a segment
/// boundary, so <c>/v1beta</c> is not under <c>/v1</c>.
/// </summary>
internal static class ProviderEndpoint
{
    internal static bool IsOwn(Uri endpoint, Uri baseUrl)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(baseUrl);

        if (
            !string.Equals(endpoint.Scheme, baseUrl.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(endpoint.Host, baseUrl.Host, StringComparison.OrdinalIgnoreCase)
            || endpoint.Port != baseUrl.Port
        )
        {
            return false;
        }

        string endpointPath = NormalizePath(endpoint);
        string basePath = NormalizePath(baseUrl);
        return basePath.Length == 0
            || string.Equals(endpointPath, basePath, StringComparison.Ordinal)
            || endpointPath.StartsWith(basePath + "/", StringComparison.Ordinal);
    }

    private static string NormalizePath(Uri uri) => uri.AbsolutePath.TrimEnd('/');
}
