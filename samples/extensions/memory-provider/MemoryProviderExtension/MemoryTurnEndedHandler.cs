using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace MemoryProviderExtension;

/// <summary>
/// Commits the capture buffer at the turn boundary through the registered <c>memory-store</c>
/// service and appends one audit extension entry carrying the number of memories committed in
/// this turn.
/// </summary>
public sealed class MemoryTurnEndedHandler(MemoryStore store) : ITurnEndedHandler
{
    public int Priority => 0;

    public ValueTask<TurnEndedResult> HandleAsync(
        TurnEndedPayload payload,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(payload);

        int committed = store.Commit();
        TurnEndedEntry entry = new(
            "committed",
            JsonSerializer.SerializeToElement(new { count = committed })
        );
        return ValueTask.FromResult<TurnEndedResult>(
            new TurnEndedResult.TurnEnded([entry], RequestContinuation: false)
        );
    }
}
