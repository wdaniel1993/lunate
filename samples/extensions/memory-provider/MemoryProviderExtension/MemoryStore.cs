using System.Globalization;
using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace MemoryProviderExtension;

/// <summary>One remembered fact and when it was added to the buffer.</summary>
public sealed record Memory(string Text, DateTimeOffset RememberedAt);

/// <summary>
/// The sample's session-scoped memory store, registered as the <c>memory-store</c> service.
/// <see cref="Add"/> buffers a memory idempotently (case-insensitive exact-text match);
/// <see cref="Commit"/> moves the buffer into the committed list, bounded by <c>maxMemories</c>
/// (oldest dropped first). The store is in-memory for the session on purpose: persistence is the
/// extension's own concern (a per-extension data directory is a candidate future primitive).
/// </summary>
public sealed class MemoryStore(string extensionId, IExtensionSettings settings, IExtensionLog log)
    : IBackgroundService
{
    private const int DefaultMaxMemories = 20;

    private readonly List<Memory> _committed = [];
    private readonly List<Memory> _buffer = [];

    /// <summary>The configured cap on committed memories (default 20).</summary>
    public int MaxMemories { get; } = ReadMaxMemories(settings);

    /// <summary>How often the host started this service.</summary>
    public int Starts { get; private set; }

    /// <summary>How often the host stopped this service.</summary>
    public int Stops { get; private set; }

    /// <summary>The committed memories, oldest first.</summary>
    public IReadOnlyList<Memory> Memories => _committed;

    /// <summary>Buffers a memory unless it is already known; returns whether it was added.</summary>
    public bool Add(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        string trimmed = text.Trim();
        if (trimmed.Length == 0 || Contains(trimmed))
        {
            return false;
        }

        _buffer.Add(new Memory(trimmed, DateTimeOffset.UtcNow));
        log.Info($"memory-provider[{extensionId}]: remembered: {trimmed}");
        return true;
    }

    /// <summary>
    /// Moves the buffer into the committed list, drops the oldest memories beyond
    /// <see cref="MaxMemories"/> and returns how many memories were committed.
    /// </summary>
    public int Commit()
    {
        int count = _buffer.Count;
        if (count == 0)
        {
            return 0;
        }

        _committed.AddRange(_buffer);
        _buffer.Clear();
        int excess = _committed.Count - MaxMemories;
        if (excess > 0)
        {
            _committed.RemoveRange(0, excess);
        }

        log.Info(
            $"memory-provider[{extensionId}]: committed {count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? "memory" : "memories")}; the store holds {_committed.Count.ToString(CultureInfo.InvariantCulture)}."
        );
        return count;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        Starts++;
        log.Info(
            $"memory-provider[{extensionId}]: service started (start #{Starts.ToString(CultureInfo.InvariantCulture)})"
        );
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        Stops++;
        log.Info(
            $"memory-provider[{extensionId}]: service stopped (stop #{Stops.ToString(CultureInfo.InvariantCulture)})"
        );
        return ValueTask.CompletedTask;
    }

    private bool Contains(string text) =>
        _committed
            .Concat(_buffer)
            .Any(memory => string.Equals(memory.Text, text, StringComparison.OrdinalIgnoreCase));

    private static int ReadMaxMemories(IExtensionSettings settings) =>
        settings.TryGet("maxMemories", out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out int max)
        && max >= 0
            ? max
            : DefaultMaxMemories;
}
