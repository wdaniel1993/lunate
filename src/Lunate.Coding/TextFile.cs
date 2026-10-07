using System.Text;

namespace Lunate.Coding;

internal static class TextFile
{
    private const int BinaryProbeSize = 8192;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static bool IsBinary(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[BinaryProbeSize];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return buffer.AsSpan(0, total).Contains((byte)0);
    }

    public static string ReadAllText(string path) => File.ReadAllText(path, Utf8NoBom);

    public static string[] ReadAllLines(string path) => SplitLines(ReadAllText(path));

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
