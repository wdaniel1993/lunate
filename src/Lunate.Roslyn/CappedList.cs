namespace Lunate.Roslyn;

/// <summary>
/// Collects items up to a cap while counting every item added; the extras are dropped so a
/// misbehaving workspace can never grow the result without bound.
/// </summary>
internal sealed class CappedList<T>(int capacity)
{
    private readonly List<T> _items = new(Math.Min(capacity, 16));

    /// <summary>The retained items, in the order they were added; at most <c>capacity</c> of them.</summary>
    public IReadOnlyList<T> Items => _items;

    /// <summary>Every item added, including the dropped ones.</summary>
    public int Total { get; private set; }

    /// <summary>True when items were dropped.</summary>
    public bool Truncated => Total > _items.Count;

    public void Add(T item)
    {
        Total++;
        if (_items.Count < capacity)
        {
            _items.Add(item);
        }
    }
}
