using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

public sealed class LruCacheTests
{
    [Fact]
    public void Evicts_and_disposes_the_least_recently_used_entry()
    {
        using var cache = new LruCache<FakeDisposable>(capacity: 2);
        var first = new FakeDisposable();
        var second = new FakeDisposable();
        var third = new FakeDisposable();

        cache.Set("a", first);
        cache.Set("b", second);
        cache.Set("c", third);

        Assert.True(first.Disposed);
        Assert.False(second.Disposed);
        Assert.False(third.Disposed);
        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void Reading_an_entry_refreshes_its_recency()
    {
        using var cache = new LruCache<FakeDisposable>(capacity: 2);
        var first = new FakeDisposable();
        var second = new FakeDisposable();
        var third = new FakeDisposable();

        cache.Set("a", first);
        cache.Set("b", second);
        Assert.True(cache.TryGet("a", out _));
        cache.Set("c", third);

        Assert.True(second.Disposed);
        Assert.False(first.Disposed);
        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public void Disposing_the_cache_disposes_every_entry()
    {
        var cache = new LruCache<FakeDisposable>(capacity: 2);
        var first = new FakeDisposable();
        var second = new FakeDisposable();
        cache.Set("a", first);
        cache.Set("b", second);

        cache.Dispose();

        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    private sealed class FakeDisposable : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
