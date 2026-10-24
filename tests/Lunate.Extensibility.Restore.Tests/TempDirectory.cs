namespace Lunate.Extensibility.Restore.Tests;

/// <summary>A scratch root for one test, removed on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    internal TempDirectory()
    {
        Root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "lunate-restore-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Root);
    }

    internal string Root { get; }

    internal string Path(string name) => System.IO.Path.Combine(Root, name);

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
