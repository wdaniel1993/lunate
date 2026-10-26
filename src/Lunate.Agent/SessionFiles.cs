using System.Text;
using Lunate.Ai;

namespace Lunate.Agent;

/// <summary>
/// The one place a session-owned file is opened, so every handle states its share mode once.
/// </summary>
/// <remarks>
/// Windows enforces sharing and POSIX does not, which is why a handle-discipline bug only shows up
/// on windows-latest. An open succeeds only when both directions hold: the access the caller asks
/// for is allowed by every existing handle's share mode, and the share mode the caller asks for
/// allows every existing handle's access. A reader that requests <see cref="FileShare.Read"/> alone
/// therefore denies a writer that already holds the file, and a writer that requests
/// <see cref="FileShare.Read"/> alone denies a reader that holds it — the second open fails with
/// "the file is being used by another process".
/// <para>
/// Readers here admit other readers, a live appending writer and a concurrent delete. Writers
/// admit readers and a concurrent delete (a rename over or of the file), but never a second
/// writer: a session has exactly one writer, and refusing the second one is the invariant, not an
/// accident.
/// </para>
/// </remarks>
internal static class SessionFiles
{
    /// <summary>Readers: any number of readers, one appending writer, and a later move or delete.</summary>
    internal const FileShare ReaderShare = FileShare.ReadWrite | FileShare.Delete;

    /// <summary>Writers: readers keep reading and the file stays renameable; a second writer is refused.</summary>
    internal const FileShare WriterShare = FileShare.Read | FileShare.Delete;

    /// <summary>UTF-8 without a BOM: the session format's encoding, byte for byte.</summary>
    internal static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Opens a session file for reading; the caller owns the stream.</summary>
    internal static FileStream OpenRead(string path) =>
        new(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = ReaderShare,
            }
        );

    /// <summary>Opens a session file to append to it; the caller owns the stream.</summary>
    internal static FileStream OpenAppend(string path)
    {
        var stream = SecureFiles.OpenOwnerOnly(
            path,
            FileMode.Append,
            FileAccess.Write,
            WriterShare
        );
        SecureFiles.RestrictToOwner(path);
        return stream;
    }

    /// <summary>Creates (or truncates) a session file for writing; the caller owns the stream.</summary>
    internal static FileStream Create(string path)
    {
        var stream = SecureFiles.OpenOwnerOnly(
            path,
            FileMode.Create,
            FileAccess.Write,
            WriterShare
        );
        // An older session file predates the owner-only mode; every rewrite repairs it.
        SecureFiles.RestrictToOwner(path);
        return stream;
    }

    /// <summary>
    /// Creates a session-owned file that must be new (a spill file's temporary copy): 0600 on POSIX,
    /// the profile's inherited ACL on Windows, and no sharing with a second writer.
    /// </summary>
    internal static FileStream CreateNewOwnerOnly(string path) =>
        SecureFiles.OpenOwnerOnly(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);

    /// <summary>
    /// Opens a session file as a text reader with the invariant's share mode. The reader detects a
    /// byte-order mark like <see cref="File.ReadAllText(string)"/> does, so a BOM-prefixed fixture
    /// still parses.
    /// </summary>
    internal static StreamReader OpenTextReader(string path, Encoding? encoding = null) =>
        new(OpenRead(path), encoding ?? Utf8NoBom);

    /// <summary>Reads a whole session file as text; the handle is closed before this returns.</summary>
    internal static string ReadText(string path, Encoding? encoding = null)
    {
        using StreamReader reader = OpenTextReader(path, encoding);
        return reader.ReadToEnd();
    }

    /// <summary>Writes a whole session file as UTF-8 without a BOM; the handle closes before this returns.</summary>
    internal static void WriteText(string path, string text)
    {
        using FileStream stream = Create(path);
        stream.Write(Utf8NoBom.GetBytes(text));
    }

    /// <summary>Appends UTF-8 without a BOM; the handle closes before this returns, so a following rename succeeds.</summary>
    internal static void AppendText(string path, string text)
    {
        using FileStream stream = OpenAppend(path);
        stream.Write(Utf8NoBom.GetBytes(text));
    }

    /// <summary>
    /// The claim a writer holds on a session file: <c>&lt;stem&gt;.lock</c> beside it, the naming
    /// the title sidecar already uses. A file of its own rather than the session file, because a
    /// writer that held the session file exclusively would also refuse the readers that list,
    /// summarise and resume from it.
    /// </summary>
    internal static string WriterLockPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.ChangeExtension(path, ".lock");
    }

    /// <summary>
    /// Opens a session file's writer lock and holds it exclusively. Windows enforces the share mode
    /// and POSIX does not once a writer admits readers, so the claim is a separate file opened with
    /// <see cref="FileShare.None"/> — the one share mode both platforms refuse to hand out twice,
    /// in this process or another.
    /// </summary>
    internal static FileStream OpenWriterLock(string path)
    {
        string lockPath = WriterLockPath(path);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(lockPath));
        if (!string.IsNullOrEmpty(directory))
        {
            SecureFiles.CreateOwnerOnlyDirectory(directory);
        }

        try
        {
            FileStream stream = SecureFiles.OpenOwnerOnly(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None
            );
            SecureFiles.RestrictToOwner(lockPath);
            return stream;
        }
        catch (IOException exception) when (File.Exists(lockPath))
        {
            throw new InvalidOperationException(
                $"the session file '{path}' is being written by another Lunate process (writer lock "
                    + $"'{lockPath}'); a session file has one writer, so finish that session or pass "
                    + "a different --session.",
                exception
            );
        }
    }

    /// <summary>
    /// Cuts a session file after its last line terminator, dropping the bytes of a record whose
    /// append never finished. The cut is found in the bytes, not in the decoded text: a fragment
    /// that ended mid-sequence decodes to a replacement character, whose width says nothing about
    /// how many bytes are there.
    /// </summary>
    internal static void CutToLastRecord(string path)
    {
        // Opened, not created: the file exists and already carries the session's mode, and
        // SecureFiles' create-time mode applies only to a file a call creates.
        using FileStream stream = new(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.ReadWrite,
                Share = WriterShare,
            }
        );
        stream.SetLength(LastTerminator(stream));
    }

    private static long LastTerminator(FileStream stream)
    {
        byte[] buffer = new byte[8192];
        long position = stream.Length;
        while (position > 0)
        {
            int take = (int)Math.Min(buffer.Length, position);
            position -= take;
            stream.Seek(position, SeekOrigin.Begin);
            stream.ReadExactly(buffer, 0, take);
            for (int index = take - 1; index >= 0; index--)
            {
                if (buffer[index] == (byte)'\n')
                {
                    return position + index + 1;
                }
            }
        }

        return 0;
    }
}
