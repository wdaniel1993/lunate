using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

public sealed partial class AgentHarness
{
    /// <summary>
    /// Calls the model and retries transient failures with exponential backoff. Only attempts that
    /// emitted nothing retry: a stream that already produced events is never replayed.
    /// </summary>
    private async Task<List<ChatResponseUpdate>> StreamModelWithRetriesAsync(
        string runId,
        AgentEventChannel channel,
        CancellationToken ct
    )
    {
        int retry = 0;
        while (true)
        {
            ModelStreamAttempt attempt = new();
            try
            {
                return await StreamModelAsync(runId, channel, ct, attempt);
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                if (
                    attempt.Emitted
                    || retry >= _options.MaxRetries
                    || !ProviderErrors.IsRetryable(exception)
                )
                {
                    throw;
                }

                channel.Emit(new Retrying(runId, retry + 1, exception.Message));
                await Task.Delay(_options.RetryBaseDelay * Math.Pow(2, retry), ct);
                retry++;
            }
        }
    }

    /// <summary>Mutable per-attempt state: set as soon as the attempt emits any event.</summary>
    private sealed class ModelStreamAttempt
    {
        public bool Emitted { get; set; }
    }
}
