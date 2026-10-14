using Lunate.Extensibility.Abstractions;
using Microsoft.Extensions.AI;

namespace ModelRouterExtension;

/// <summary>The counting middleware's state, exposed through the <c>router-counters</c> service.</summary>
internal sealed class RouterCounters
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _callsByModel = new(StringComparer.Ordinal);
    private int _total;

    /// <summary>Every backend call the middleware saw.</summary>
    public int Total
    {
        get
        {
            lock (_gate)
            {
                return _total;
            }
        }
    }

    /// <summary>Calls per wire model id the backend reported.</summary>
    public IReadOnlyDictionary<string, int> CallsByModel
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, int>(_callsByModel, StringComparer.Ordinal);
            }
        }
    }

    public void Record(string? modelId)
    {
        string key = string.IsNullOrEmpty(modelId) ? "unknown" : modelId;
        lock (_gate)
        {
            _total++;
            _callsByModel[key] = _callsByModel.TryGetValue(key, out int count) ? count + 1 : 1;
        }
    }
}

/// <summary>
/// The session-scoped service other extensions (and tests) use to read the counts; it starts and
/// stops doing nothing, like every observers-only service.
/// </summary>
internal sealed class RouterCounterService(RouterCounters counters) : IBackgroundService
{
    public int Total => counters.Total;

    public IReadOnlyDictionary<string, int> CallsByModel => counters.CallsByModel;

    public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

/// <summary>
/// The <c>AroundBackend</c> counting middleware: it wraps every resolved backend client, so it
/// sees exactly the calls that reach a backend — a virtual-model call counts once, and a direct
/// backend call counts too. The wire model id the backend reports names the backend.
/// </summary>
internal sealed class CountingClient(IChatClient inner, RouterCounters counters)
    : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        ChatResponse response = await base.GetResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);
        counters.Record(response.ModelId);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default
    )
    {
        string? model = null;
        await foreach (
            ChatResponseUpdate update in base.GetStreamingResponseAsync(
                    messages,
                    options,
                    cancellationToken
                )
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false)
        )
        {
            model ??= update.ModelId;
            yield return update;
        }

        counters.Record(model);
    }
}
