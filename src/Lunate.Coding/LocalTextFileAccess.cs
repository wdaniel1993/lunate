using System.Text;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>The default <see cref="ITextFileAccess"/>: the local disk behind <see cref="TextFile"/>.</summary>
internal sealed class LocalTextFileAccess : ITextFileAccess
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static LocalTextFileAccess Instance { get; } = new();

    public bool Exists(string path) => File.Exists(path);

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
