using System.Net;

namespace Lunate.Ai.SystemOne;

internal sealed class DecisionTransportException(
    HttpStatusCode statusCode,
    string providerMessage,
    string? field = null
) : Exception(CreateMessage(statusCode, providerMessage, field))
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ProviderMessage { get; } = providerMessage;
    public string? Field { get; } = field;

    private static string CreateMessage(HttpStatusCode statusCode, string providerMessage, string? field)
    {
        if (statusCode == HttpStatusCode.RequestEntityTooLarge)
        {
            return $"Decision state exceeds the 32 MiB request limit: {providerMessage}";
        }
        if (
            statusCode == HttpStatusCode.BadRequest
            && providerMessage.Contains("max_tokens_exceeded", StringComparison.OrdinalIgnoreCase)
        )
        {
            return $"Decision state too large for this model: {providerMessage}";
        }
        if (field is not null)
        {
            return $"System One rejected field '{field}': {providerMessage}";
        }
        return $"System One request failed with HTTP {(int)statusCode}: {providerMessage}";
    }
}
