using System.Text;

namespace Lunate.Agent;

/// <summary>
/// A session's optional title, stored beside the append-only session file as
/// <c>&lt;sessionId&gt;.title</c>: one UTF-8 line, trimmed, interior whitespace collapsed and at
/// most <see cref="MaxLength"/> characters. The sidecar keeps the JSONL format untouched, so older
/// builds and the golden sessions never see it.
/// </summary>
public static class SessionTitle
{
    /// <summary>The longest title a session may carry.</summary>
    public const int MaxLength = 120;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    /// <summary>
    /// The sidecar path for a session file: the same directory and the file's stem plus
    /// <c>.title</c> (the store names sessions <c>&lt;sessionId&gt;.jsonl</c>).
    /// </summary>
    public static string PathFor(string sessionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionPath);
        return Path.ChangeExtension(sessionPath, ".title");
    }

    /// <summary>
    /// Trims and collapses every interior whitespace run to one space. A blank title, or one longer
    /// than <see cref="MaxLength"/> characters, normalizes to null.
    /// </summary>
    public static string? Normalize(string? title)
    {
        if (title is null)
        {
            return null;
        }

        string[] words = title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return null;
        }

        string normalized = string.Join(' ', words);
        return normalized.Length <= MaxLength ? normalized : null;
    }

    /// <summary>
    /// Reads the title from the first sidecar line. A missing, unreadable, empty, invalid-UTF-8 or
    /// over-long sidecar reads as no title and never throws.
    /// </summary>
    public static string? Read(string sessionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionPath);
        try
        {
            using var reader = SessionFiles.OpenTextReader(PathFor(sessionPath), StrictUtf8);
            return reader.ReadLine() is { } line ? Normalize(line) : null;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or DecoderFallbackException
            )
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the normalized title as one UTF-8 line without a BOM, atomically (a temp file in the
    /// same directory is moved over the sidecar), so a reader never sees half a title. The session
    /// file itself is never touched.
    /// </summary>
    public static void Write(string sessionPath, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionPath);
        string normalized =
            Normalize(title)
            ?? throw new ArgumentException(
                $"A session title must be 1 to {MaxLength} characters after trimming.",
                nameof(title)
            );
        string path = PathFor(sessionPath);
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            SessionFiles.WriteText(temp, normalized + "\n");
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
