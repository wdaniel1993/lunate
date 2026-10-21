using Xunit;
using Lunate.Coding;

namespace Lunate.Coding.Tests;

public sealed class SearchScopeTests
{
    [Theory]
    [InlineData("dir", "dir/a.cs", true)]
    [InlineData("dir", "dir\\a.cs", true)]
    [InlineData("dir", "dir/nested/a.cs", true)]
    [InlineData("dir", "dir\\nested\\a.cs", true)]
    [InlineData("dir", "other/a.cs", false)]
    [InlineData("dir", "dir2/a.cs", false)]
    [InlineData("dir", "dir", true)]
    [InlineData(".", "anything/at/all.cs", true)]
    [InlineData("", "anything.cs", true)]
    public void The_scope_accepts_either_separator(string root, string path, bool expected)
    {
        // The file index yields platform separators while callers may pass either; the
        // scope rule must not depend on which one reached it.
        Assert.Equal(expected, SearchScope.Contains(root, path));
    }
}
