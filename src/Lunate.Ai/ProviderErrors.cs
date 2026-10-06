using System.ClientModel;
using Anthropic.Exceptions;

namespace Lunate.Ai;

/// <summary>
/// Classifies provider exceptions as transient. Conservative by design: only failures a retry can
/// plausibly fix are retryable; everything else surfaces as a run error with the provider's message.
/// The classifier walks the inner-exception chain so wrapped provider errors are recognized.
/// </summary>
public static class ProviderErrors
{
    /// <summary>Whether <paramref name="exception"/> is a transient provider failure worth retrying.</summary>
    public static bool IsRetryable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TaskCanceledException or TimeoutException or HttpRequestException)
            {
                return true;
            }

            if (
                current is ClientResultException clientResult
                && IsTransientStatus(clientResult.Status)
            )
            {
                return true;
            }

            if (
                current is AnthropicApiException anthropic
                && IsTransientStatus((int)anthropic.StatusCode)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTransientStatus(int status) =>
        status is 408 or 429 or 500 or 502 or 503 or 504 or 529;
}
