using Lunate.Agent;
using Lunate.TestSupport;

namespace Lunate.Coding.Tests;

/// <summary>A temp copy of a fixture solution; deleting it cleans up everything including restore output.</summary>
internal sealed class RoslynFixture(string root) : IDisposable
{
    public string Root { get; } = root;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Copies fixture solutions and restores them offline (package-free fixtures).</summary>
internal static class RoslynFixtureSupport
{
    internal static string RepositoryRoot { get; } = Lunate.TestSupport.RepositoryRoot.Find();

    internal static RoslynFixture CopySolution(string name)
    {
        var source = Path.Combine(RepositoryRoot, "tests", "fixtures", "solutions", name);
        var root = Path.Combine(
            Path.GetTempPath(),
            "lunate-coding-roslyn-tests",
            Guid.NewGuid().ToString("N")
        );
        CopyDirectory(source, root);
        // Canonicalize the temp root: on macOS Path.GetTempPath() returns the /var symlink alias,
        // and a restore graph that mixes /var and /private/var spellings restores the same project
        // twice, racing on its obj files.
        return new RoslynFixture(GitIdentity.Canonicalize(root));
    }

    /// <summary>Copies a fixture copy (restore output included) into a fresh temp root.</summary>
    internal static RoslynFixture CopyFixture(RoslynFixture fixture)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lunate-coding-roslyn-tests",
            Guid.NewGuid().ToString("N")
        );
        CopyDirectory(fixture.Root, root);
        return new RoslynFixture(GitIdentity.Canonicalize(root));
    }

    /// <summary>
    /// Restores the copied solution offline (package-free fixtures, cleared source list), reusing the
    /// artifacts of an identical tree restored earlier in this run instead of one child per test.
    /// </summary>
    internal static void Restore(RoslynFixture fixture, string solutionFileName) =>
        FixtureRestore.Restore(fixture.Root, solutionFileName);

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
