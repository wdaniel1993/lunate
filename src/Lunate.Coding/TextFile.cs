using System.Text;

namespace Lunate.Coding;

internal static class TextFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string ReadAllText(string path) => File.ReadAllText(path, Utf8NoBom);

    public static (string Text, bool HasBom) ReadRaw(string path)
    {
        // The share mode admits a concurrent writer and a replace: Windows enforces it, so a bare
        // File.ReadAllBytes (FileShare.Read) would refuse a rename that is already in flight.
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var start = hasBom ? 3 : 0;

        return (Encoding.UTF8.GetString(bytes, start, bytes.Length - start), hasBom);
    }

    public static void WriteRaw(string path, string text, bool hasBom)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        if (hasBom)
        {
            stream.Write([0xEF, 0xBB, 0xBF]);
        }

        stream.Write(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// The ending a text mostly uses: CRLF when it has more CRLF line breaks than bare ones, LF when
    /// it has more of those, and the first line's ending on a tie — so a file with mixed endings is
    /// rewritten as one file in the form it already leans towards, not as a mixture again.
    /// </summary>
    public static string DominantEnding(string text)
    {
        var crlf = 0;
        var lf = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            if (index > 0 && text[index - 1] == '\r')
            {
                crlf++;
            }
            else
            {
                lf++;
            }
        }

        if (crlf == lf)
        {
            return FirstEnding(text);
        }

        return crlf > lf ? "\r\n" : "\n";
    }

    private static string FirstEnding(string text)
    {
        var newline = text.IndexOf('\n');

        return newline > 0 && text[newline - 1] == '\r' ? "\r\n" : "\n";
    }

    public static string[] SplitLines(string text)
    {
        var raw = text.Split('\n');
        var count = raw.Length;
        if (count > 0 && raw[^1].Length == 0)
        {
            count--;
        }

        var lines = new string[count];
        for (var index = 0; index < count; index++)
        {
            var line = raw[index];
            lines[index] = line.EndsWith('\r') ? line[..^1] : line;
        }

        return lines;
    }
}
