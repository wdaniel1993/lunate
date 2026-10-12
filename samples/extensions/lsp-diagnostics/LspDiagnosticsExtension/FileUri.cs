using System.Globalization;

namespace LspDiagnosticsExtension;

/// <summary>
/// Converts absolute file paths to the <c>file:</c> URIs LSP documents are keyed by. Pure and
/// separator-tolerant: Windows drive paths (<c>C:\src\Foo.cs</c>) and POSIX paths
/// (<c>/src/Foo.cs</c>) both convert, segments are percent-encoded.
/// </summary>
public static class FileUri
{
    /// <summary>The <c>file:</c> URI of an absolute path (both separators accepted).</summary>
    public static string FromPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string slash = path.Replace('\\', '/');
        bool drive = slash.Length >= 2 && char.IsAsciiLetter(slash[0]) && slash[1] == ':';
        string prefix = drive
            ? "file:///"
                + char.ToUpperInvariant(slash[0]).ToString(CultureInfo.InvariantCulture)
                + ':'
            : "file://";
        string rest = drive ? slash[2..] : slash;
        string encoded = string.Join(
            '/',
            rest.Split('/', StringSplitOptions.None).Select(Uri.EscapeDataString)
        );
        return prefix + encoded;
    }
}
