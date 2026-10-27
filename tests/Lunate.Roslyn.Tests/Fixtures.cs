using Lunate.TestSupport;

namespace Lunate.Roslyn.Tests;

/// <summary>A temp copy of a fixture solution; deleting it cleans up everything including restore output.</summary>
internal sealed class FixtureSolution(string root) : IDisposable
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

internal static class Fixtures
{
    public static string RepositoryRoot { get; } = Lunate.TestSupport.RepositoryRoot.Find();

    public static FixtureSolution CopySolution(string name)
    {
        var source = Path.Combine(RepositoryRoot, "tests", "fixtures", "solutions", name);
        var root = Path.Combine(
            Path.GetTempPath(),
            "lunate-roslyn-tests",
            Guid.NewGuid().ToString("N")
        );
        CopyDirectory(source, root);
        // Canonicalize the temp root: on macOS Path.GetTempPath() returns the /var symlink alias,
        // and a restore graph that mixes /var and /private/var spellings restores the same project
        // twice, racing on its obj files.
        return new FixtureSolution(PathIdentity.Canonicalize(root));
    }

    /// <summary>
    /// Restores the copied solution offline (package-free fixtures, cleared source list), reusing the
    /// artifacts of an identical tree restored earlier in this run instead of one child per test.
    /// </summary>
    public static void Restore(FixtureSolution fixture, string solutionFileName) =>
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
            var name = Path.GetFileName(directory);
            if (name is "bin" or "obj")
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(destination, name));
        }
    }
}
