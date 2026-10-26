using Lunate.TestSupport;

namespace Lunate.Coding.Tests;

/// <summary>
/// The contract the suites now share: one walk, one marker, one message. The negative case starts at
/// the filesystem root, so it holds wherever the repository is checked out.
/// </summary>
public sealed class RepositoryRootTests
{
    [Fact]
    public void Find_returns_the_directory_holding_the_solution_file()
    {
        string root = RepositoryRoot.Find();

        Assert.True(File.Exists(Path.Combine(root, RepositoryRoot.Marker)));
        Assert.True(Directory.Exists(Path.Combine(root, "tests")));
    }

    [Fact]
    public void Find_walks_up_from_a_nested_directory()
    {
        string root = RepositoryRoot.Find();

        Assert.Equal(root, RepositoryRoot.Find(Path.Combine(root, "tests", "Lunate.Coding.Tests")));
    }

    [Fact]
    public void Find_from_the_filesystem_root_names_the_marker_and_the_start_directory()
    {
        string start = Path.GetPathRoot(RepositoryRoot.Find())!;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
        {
            RepositoryRoot.Find(start);
        });

        Assert.Contains(RepositoryRoot.Marker, error.Message, StringComparison.Ordinal);
        Assert.Contains($"'{start}'", error.Message, StringComparison.Ordinal);
    }
}
