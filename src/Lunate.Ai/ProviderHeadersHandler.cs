namespace Lunate.Ai;

/// <summary>
/// One of the two DelegatingHandlers on every provider transport (one per created provider
/// client): this one always writes <c>User-Agent: lunate/&lt;version&gt;</c> over any SDK default
/// and then the provider's declared headers, each value substituted from the run's context — a
/// template that needs a session id with none present fails loudly. A declared header replaces
/// what the request already carries of the same name: the provider wins over a protocol header
/// (the SDK's own <c>anthropic-version</c> stays untouched unless the provider declares it).
/// Resolution runs at request time, so a client can outlive a run.
/// </summary>
internal sealed class ProviderHeadersHandler : DelegatingHandler
{
    private readonly IReadOnlyList<(string Name, string Raw)> _headers;
    private readonly RequestContext? _context;
    private readonly Func<string, string?>? _namedKeySource;
    private readonly Func<string, string?> _environment;
    private readonly string _userAgent;

    internal ProviderHeadersHandler(
        IEnumerable<(string Name, string Raw)> headers,
        RequestContext? context,
        Func<string, string?>? namedKeySource,
        Func<string, string?>? environment
    )
    {
        _headers = [.. headers];
        _context = context;
        _namedKeySource = namedKeySource;
        _environment = environment ?? Environment.GetEnvironmentVariable;
        _userAgent = RequestContext.LunateVersion;
    }

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
        // One user agent, ours: the SDK's own UA is removed first, so the wire carries exactly
        // one agent string and it names this client.
        request.Headers.Remove("User-Agent");
        request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);

        foreach ((string name, string raw) in _headers)
        {
            string value = Render(raw, name);
            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private string Render(string raw, string name) =>
        ProviderHeaderValues.RenderTemplate(
            raw,
            name,
            RequestContext.Current ?? _context,
            _namedKeySource,
            _environment
        );
}
