namespace Lunate.Ai;

/// <summary>
/// The second handler of the pair on every provider transport: owns the credential and places it
/// exactly once, per the declared placement — an Authorization-style header (the default), another
/// named header, or a query parameter. It first removes the credential headers an SDK could have
/// added (<c>Authorization</c>, <c>x-api-key</c>, <c>x-goog-api-key</c>; the SDK builds every
/// request with a non-secret sentinel key), so a key appears once, in the declared place. A
/// declared provider header of the same name has already run: the placement wins, because the
/// credential is not template data.
/// </summary>
internal sealed class ProviderCredentialHandler(string credential, CredentialPlacement placement)
    : DelegatingHandler
{
    /// <summary>The credential headers an SDK client can have added at construction.</summary>
    private static readonly string[] SdkCredentialHeaders =
    [
        "Authorization",
        "x-api-key",
        "x-goog-api-key",
    ];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Apply(request);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private void Apply(HttpRequestMessage request)
    {
        foreach (string name in SdkCredentialHeaders.Append(placement.Name))
        {
            request.Headers.Remove(name);
        }

        switch (placement.Target)
        {
            case CredentialPlacementTarget.Header:
                request.Headers.TryAddWithoutValidation(
                    placement.Name,
                    placement.HeaderValue(credential)
                );
                break;
            case CredentialPlacementTarget.Query:
                request.RequestUri = WithQueryParameter(
                    request.RequestUri,
                    credential,
                    placement.Name
                );
                break;
        }
    }

    private static Uri WithQueryParameter(Uri? requestUri, string credential, string name)
    {
        if (requestUri is null)
        {
            throw new InvalidOperationException(
                "The provider places the credential in a query parameter, but the request has no URI."
            );
        }

        var builder = new UriBuilder(requestUri);
        string addition = FormattableString.Invariant(
            $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(credential)}"
        );
        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? addition
            : builder.Query.TrimStart('?') + "&" + addition;
        return builder.Uri;
    }
}
