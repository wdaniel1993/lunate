namespace Lunate.Roslyn;

internal sealed class LruCache<TValue>(int capacity) : IDisposable
    where TValue : IDisposable
{
    private readonly Dictionary<string, LinkedListNode<Entry>> _map = new(PathIdentity.Comparer);
    private readonly LinkedList<Entry> _order = [];

    public bool TryGet(string key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default!;
        return false;
    }

    public void Set(string key, TValue value)
    {
        if (_map.Remove(key, out var existing))
        {
            _order.Remove(existing);
            existing.Value.Value.Dispose();
        }

        var node = _order.AddFirst(new Entry(key, value));
        _map[key] = node;

        while (_order.Count > capacity)
        {
            var last = _order.Last!;
            _order.RemoveLast();
            _map.Remove(last.Value.Key);
            last.Value.Value.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var entry in _order)
        {
            entry.Value.Dispose();
        }

        _order.Clear();
        _map.Clear();
    }

    private sealed record Entry(string Key, TValue Value);
}
