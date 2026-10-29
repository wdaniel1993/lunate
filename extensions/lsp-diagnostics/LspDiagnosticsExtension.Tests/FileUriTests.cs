using LspDiagnosticsExtension;

namespace LspDiagnosticsExtension.Tests;

public sealed class FileUriTests
{
    [Theory]
    [InlineData(@"C:\src\Foo.cs", "file:///C:/src/Foo.cs")]
    [InlineData(@"c:\src\Foo.cs", "file:///C:/src/Foo.cs")]
    [InlineData(@"C:\src\my dir\Foo.cs", "file:///C:/src/my%20dir/Foo.cs")]
    [InlineData("/src/Foo.cs", "file:///src/Foo.cs")]
    [InlineData("/src/my dir/Foo.cs", "file:///src/my%20dir/Foo.cs")]
    public void Absolute_paths_convert_to_file_uris(string path, string expected)
    {
        Assert.Equal(expected, FileUri.FromPath(path));
    }
}
