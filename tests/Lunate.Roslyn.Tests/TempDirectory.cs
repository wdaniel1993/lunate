namespace Lunate.Roslyn.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "lunate-roslyn-tests",
            Guid.NewGuid().ToString("N")
        );
        System.IO.Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string File(string name) => Path.Combine(Root, name);

    public string Subdirectory(string name)
    {
        var path = Path.Combine(Root, name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

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
