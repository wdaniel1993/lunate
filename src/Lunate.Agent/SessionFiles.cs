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
}
