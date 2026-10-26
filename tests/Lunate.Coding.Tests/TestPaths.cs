namespace Lunate.Coding.Tests;

internal static class TestPaths
{
    public static string RepositoryRoot { get; } = Find();

    private static string Find()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "lunate.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not find lunate.slnx above {AppContext.BaseDirectory}."
        );
    }
}
