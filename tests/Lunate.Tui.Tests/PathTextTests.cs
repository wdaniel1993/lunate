namespace Lunate.Tui.Tests;

public sealed class PathTextTests
{
    [Fact]
    public void A_short_path_is_unchanged() =>
        Assert.Equal("src/a.cs", PathText.Shorten("src/a.cs", 40));

    [Fact]
    public void The_home_directory_becomes_a_tilde() =>
        Assert.Equal("~/dev/a.cs", PathText.Shorten("/Users/daniel/dev/a.cs", 40, "/Users/daniel"));

    [Fact]
    public void The_home_directory_itself_becomes_a_tilde() =>
        Assert.Equal("~", PathText.Shorten("/Users/daniel", 40, "/Users/daniel"));

    [Fact]
    public void A_sibling_sharing_the_home_prefix_is_not_tildified() =>
        Assert.Equal("/Users/daniel/a.cs", PathText.Shorten("/Users/daniel/a.cs", 40, "/Users/dan"));

    [Fact]
    public void A_path_outside_home_keeps_its_full_form() =>
        Assert.Equal("/etc/hosts", PathText.Shorten("/etc/hosts", 40, "/Users/daniel"));

    [Fact]
    public void A_long_path_elides_in_the_middle_keeping_the_basename() =>
        Assert.Equal(
            "~/dev/l\u2026/Session.cs",
            PathText.Shorten(
                "/Users/daniel/dev/lunate/src/Lunate.Tui/Session.cs",
                19,
                "/Users/daniel"
            )
        );

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(19)]
    [InlineData(24)]
    [InlineData(40)]
    public void The_result_never_exceeds_its_budget(int budget)
    {
        string shortened = PathText.Shorten(
            "/Users/daniel/dev/lunate/src/Lunate.Tui/Session.cs",
            budget,
            "/Users/daniel"
        );

        Assert.InRange(shortened.Length, 1, budget);
    }

    [Fact]
    public void A_budget_below_the_basename_keeps_the_tail() =>
        Assert.Equal("\u2026cc.cc", PathText.Shorten("/a/bb/cc.cc", 6));

    [Fact]
    public void A_separatorless_value_keeps_its_tail() =>
        Assert.Equal("\u2026fgh.cs", PathText.Shorten("abcdefgh.cs", 6));

    [Fact]
    public void Backslash_separators_elide_too() =>
        Assert.Equal(
            @"~\dev\…\Session.cs",
            PathText.Shorten(@"C:\Users\dan\dev\lunate\src\Session.cs", 18, @"C:\Users\dan")
        );

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_empty_path_stays_empty(string? path) =>
        Assert.Equal("", PathText.Shorten(path!, 40));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_nonpositive_budget_stays_empty(int budget) =>
        Assert.Equal("", PathText.Shorten("src/a.cs", budget));
}
