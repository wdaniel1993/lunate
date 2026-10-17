namespace NodeRemoteExtension.Tests;

/// <summary>The sample's source directory (the manifest and script live there, not in the output).</summary>
internal static class SampleExtension
{
    public static string Directory { get; } =
        Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "extensions",
            "node-remote",
            "NodeRemoteExtension"
        );

    private static string FindRepositoryRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "lunate.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not find lunate.sln above {AppContext.BaseDirectory}."
        );
    }
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "lunate-node-remote-tests",
            Guid.NewGuid().ToString("N")
        );
        System.IO.Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
