using Lunate.Extensibility.Abstractions;

namespace MemoryProviderExtension;

/// <summary>
/// Captures every user line starting with <c>remember:</c> (case-insensitive after trim) into the
/// store's buffer and injects the committed store as one request-local <c>system</c> message with a
/// <c>## Memory</c> heading. Capture is idempotent, so re-scanning history on later requests never
/// duplicates. Memories captured while building request N are committed at that turn's end and
/// injected from request N+1.
/// </summary>
public sealed class MemoryContextBuildingHandler(
    string extensionId,
    MemoryStore store,
    IExtensionLog log
) : IContextBuildingHandler
{
    private const string Marker = "remember:";
    private const string Heading = "## Memory";

    public int Priority => 0;

    public ValueTask<ContextBuildingResult> HandleAsync(
        ContextBuildingPayload payload,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(payload);

        foreach (ContextMessage message in payload.Messages)
        {
            if (string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                Capture(message.Text);
            }
        }

        IReadOnlyList<Memory> memories = store.Memories;
        int injected = Math.Min(memories.Count, store.MaxMemories);
        if (injected == 0)
        {
            return ValueTask.FromResult(ContextBuildingResult.None);
        }

        List<string> lines = [Heading, .. memories.Take(injected).Select(memory => $"- {memory.Text}")];
        log.Info(
            $"memory-provider[{extensionId}]: injected {injected} {(injected == 1 ? "memory" : "memories")}"
        );
        return ValueTask.FromResult(
            new ContextBuildingResult([new ContextMessage("system", string.Join("\n", lines))])
        );
    }

    private void Capture(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith(Marker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string memory = trimmed[Marker.Length..].Trim();
            if (memory.Length > 0)
            {
                store.Add(memory);
            }
        }
    }
}
