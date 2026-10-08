using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

public sealed class DocumentFreshnessTests
{
    [Fact]
    public void An_untouched_tracked_file_is_not_reported()
    {
        using var temp = new TempDirectory();
        var file = temp.File("a.cs");
        System.IO.File.WriteAllText(file, "class A { }");
        var freshness = new DocumentFreshness();
        freshness.Track(file);

        var changed = freshness.TakeChanged([file]);

        Assert.Empty(changed);
    }

    [Fact]
    public void A_length_change_is_reported_once()
    {
        using var temp = new TempDirectory();
        var file = temp.File("a.cs");
        System.IO.File.WriteAllText(file, "class A { }");
        var freshness = new DocumentFreshness();
        freshness.Track(file);

        System.IO.File.WriteAllText(file, "class A { int x; }");

        Assert.Equal([file], freshness.TakeChanged([file]));
        Assert.Empty(freshness.TakeChanged([file]));
    }

    [Fact]
    public void A_timestamp_change_with_the_same_length_is_reported()
    {
        using var temp = new TempDirectory();
        var file = temp.File("a.cs");
        System.IO.File.WriteAllText(file, "class A { }");
        var freshness = new DocumentFreshness();
        freshness.Track(file);

        System.IO.File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));

        Assert.Equal([file], freshness.TakeChanged([file]));
    }

    [Fact]
    public void A_deleted_file_is_reported()
    {
        using var temp = new TempDirectory();
        var file = temp.File("a.cs");
        System.IO.File.WriteAllText(file, "class A { }");
        var freshness = new DocumentFreshness();
        freshness.Track(file);

        System.IO.File.Delete(file);

        Assert.Equal([file], freshness.TakeChanged([file]));
    }

    [Fact]
    public void Independent_files_are_tracked_separately()
    {
        using var temp = new TempDirectory();
        var first = temp.File("a.cs");
        var second = temp.File("b.cs");
        System.IO.File.WriteAllText(first, "class A { }");
        System.IO.File.WriteAllText(second, "class B { }");
        var freshness = new DocumentFreshness();
        freshness.Track(first);
        freshness.Track(second);

        System.IO.File.WriteAllText(second, "class B { int x; }");

        Assert.Equal([second], freshness.TakeChanged([first, second]));
    }
}
