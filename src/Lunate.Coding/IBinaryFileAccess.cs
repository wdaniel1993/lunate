namespace Lunate.Coding;

/// <summary>
/// The byte seam the read tool uses for images. A client-backed <see cref="Agent.ITextFileAccess"/>
/// may implement it when it can serve bytes; when it does not, the read tool keeps the text and
/// binary behavior.
/// </summary>
internal interface IBinaryFileAccess
{
    /// <summary>The file's total length in bytes.</summary>
    long Length(string path);

    /// <summary>Reads at most <paramref name="maxBytes"/> bytes from the start of the file.</summary>
    byte[] ReadPrefixBytes(string path, int maxBytes);

    /// <summary>Reads the whole file as bytes.</summary>
    byte[] ReadAllBytes(string path);
}
