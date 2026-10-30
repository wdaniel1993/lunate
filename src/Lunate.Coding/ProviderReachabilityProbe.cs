namespace Lunate.Coding;

internal enum ProviderReachability
{
    Reachable,
    Unreachable,
    Unknown,
}

/// <summary>A bounded unauthenticated connectivity probe for a provider endpoint.</summary>
internal static class ProviderReachabilityProbe
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    public static ProviderReachability Check(HttpClient client, Uri endpoint, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(endpoint);
        TimeSpan boundedTimeout =
            timeout <= TimeSpan.Zero || timeout > DefaultTimeout ? DefaultTimeout : timeout;

        using var cancellation = new CancellationTokenSource(boundedTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Head, endpoint);
        try
        {
            using HttpResponseMessage _ = client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token)
                .WaitAsync(boundedTimeout)
                .GetAwaiter()
                .GetResult();
            return ProviderReachability.Reachable;
        }
        catch (HttpRequestException)
        {
            return ProviderReachability.Unreachable;
        }
        catch (OperationCanceledException)
        {
            return ProviderReachability.Unknown;
        }
        catch (TimeoutException)
        {
            return ProviderReachability.Unknown;
        }
    }
}
