using System.Text;
using Lunate.Ai;

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

    /// <summary>
    /// Writes a file's text without ever truncating the destination: the bytes go to a temporary file
    /// beside it, are flushed to disk, take the original's permissions, and are moved into place in
    /// one step. A crash, a full disk or a replace another process blocks leaves the original file
    /// intact. A path that is a symlink writes through to its target; the link itself is kept.
    /// </summary>
    public static void WriteRaw(string path, string text, bool hasBom)
    {
        // Canonicalizing resolves a symlink — and a symlinked ancestor — so the write replaces the
        // real file rather than the link, and a dangling link's target is written instead of the link.
        string target = PathCanonical.Canonicalize(path);
        string directory = Path.GetDirectoryName(target) ?? ".";
        string temporary = Path.Combine(
            directory,
            "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp"
        );
        try
        {
            using (
                var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None
                )
            )
            {
                if (hasBom)
                {
                    stream.Write([0xEF, 0xBB, 0xBF]);
                }

                stream.Write(Encoding.UTF8.GetBytes(text));
                stream.Flush(flushToDisk: true);
            }

            CopyPermissions(target, temporary);
            try
            {
                FileReplacement.Replace(temporary, target, preserveTarget: true);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    $"the file '{target}' could not be replaced because another process may have it open or it is not writable. The original file is unchanged.",
                    exception
                );
            }
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary>
    /// The original's mode, when the destination already exists. Windows keeps the target's ACLs and
    /// attributes through <see cref="File.Replace(string, string, string?)"/>, so there is nothing to
    /// copy; POSIX replaces the inode, so the temporary file takes the mode before the rename.
    /// </summary>
    private static void CopyPermissions(string target, string temporary)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(target))
        {
            return;
        }

        File.SetUnixFileMode(temporary, File.GetUnixFileMode(target));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
