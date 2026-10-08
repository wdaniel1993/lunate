using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

public sealed class CappedListTests
{
    [Fact]
    public void Keeps_everything_under_the_cap()
    {
        var list = new CappedList<int>(3);

        list.Add(1);
        list.Add(2);
        list.Add(3);

        Assert.Equal(new[] { 1, 2, 3 }, list.Items);
        Assert.Equal(3, list.Total);
        Assert.False(list.Truncated);
    }

    [Fact]
    public void Drops_items_beyond_the_cap_but_keeps_the_total()
    {
        var list = new CappedList<int>(2);

        for (var i = 1; i <= 5; i++)
        {
            list.Add(i);
        }

        Assert.Equal(new[] { 1, 2 }, list.Items);
        Assert.Equal(5, list.Total);
        Assert.True(list.Truncated);
    }

    [Fact]
    public void An_empty_list_reports_no_truncation()
    {
        var list = new CappedList<string>(20);

        Assert.Empty(list.Items);
        Assert.Equal(0, list.Total);
        Assert.False(list.Truncated);
    }
}
