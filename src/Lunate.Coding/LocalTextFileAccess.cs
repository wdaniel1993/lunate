using System.Text;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>The default <see cref="ITextFileAccess"/>: the local disk behind <see cref="TextFile"/>.</summary>
internal sealed class LocalTextFileAccess : ITextFileAccess
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static LocalTextFileAccess Instance { get; } = new();

    public bool Exists(string path) => File.Exists(path);

    public (string Text, long Length)? ReadPrefix(string path, int maxBytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite
        );
        var buffer = new byte[maxBytes];
        var read = stream.ReadAtLeast(buffer, maxBytes, throwOnEndOfStream: false);
        var prefix = buffer.AsSpan(0, read);
        if (prefix.Length >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF)
        {
            prefix = prefix[3..];
        }

        return (Utf8NoBom.GetString(prefix), new FileInfo(path).Length);
    }

    public string ReadAllText(string path) => TextFile.ReadAllText(path);

    public (string Text, bool HasBom) ReadRaw(string path) => TextFile.ReadRaw(path);

    public void WriteAllText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Utf8NoBom);
    }

    public void WriteRaw(string path, string text, bool hasBom) =>
        TextFile.WriteRaw(path, text, hasBom);
}
