using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// The content-addressed image blob store of one session: files live beside the session file at
/// <c>&lt;sessions-dir&gt;/&lt;session-id&gt;/images/&lt;sha256&gt;.&lt;ext&gt;</c>. A store without a
/// session keeps the bytes in memory and writes nothing. Sessions assume a single writer, and the
/// decode cache is the only shared state read from several threads.
/// </summary>
internal sealed partial class SessionImages
{
    /// <summary>The most image bytes a store keeps decoded in memory.</summary>
    internal const long CacheLimitBytes = 16L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]>? _memory;

    private long _cacheBytes;

    private SessionImages(string? imagesDirectory)
    {
        DirectoryPath = imagesDirectory;
        _memory = imagesDirectory is null
            ? new Dictionary<string, byte[]>(StringComparer.Ordinal)
            : null;
    }

    /// <summary>The session's own image directory; null for the in-memory store.</summary>
    internal string? DirectoryPath { get; }

    /// <summary>Whether the store keeps its bytes in memory and never touches disk.</summary>
    internal bool IsInMemory => DirectoryPath is null;

    /// <summary>
    /// Empties this session's image directory for a newly created session. Only the session's own
    /// images directory is deleted; nothing outside it is touched.
    /// </summary>
    internal void ResetForNewSession()
    {
        if (DirectoryPath is not { } directory)
        {
            return;
        }

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);
    }

    /// <summary>A store that keeps images in memory; nothing is written.</summary>
    internal static SessionImages InMemory() => new(null);

    /// <summary>
    /// The store beside a session file. The session id is validated like the session file name, so
    /// a corrupt header id cannot escape the sessions directory.
    /// </summary>
    internal static SessionImages ForSession(Session session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _ = SessionPaths.SessionFileName(session.SessionId);
        string directory =
            Path.GetDirectoryName(Path.GetFullPath(session.Path))
            ?? throw new ArgumentException(
                $"Session path '{session.Path}' has no directory to place images in.",
                nameof(session)
            );
        return new SessionImages(Path.Combine(directory, session.SessionId, "images"));
    }

    /// <summary>
    /// Stores image bytes under their SHA-256 digest and returns the part a message records.
    /// Identical bytes in the same session are stored once.
    /// </summary>
    internal ImageContentReference Store(
        ReadOnlyMemory<byte> data,
        string mimeType,
        int width,
        int height
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        string extension = ExtensionFor(mimeType);
        string digest = Convert.ToHexStringLower(SHA256.HashData(data.Span));
        var reference = new ImageContentReference(digest, mimeType, width, height);

        if (_memory is not null)
        {
            lock (_gate)
            {
                if (!_memory.ContainsKey(digest))
                {
                    _memory[digest] = data.ToArray();
                }
            }

            return reference;
        }

        string directory = DirectoryPath!;
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, BlobName(digest, extension));
        if (IsUsableBlob(target, data.Length))
        {
            return reference;
        }

        // A target that exists but is a link, short or otherwise unusable is corrupt: remove it and
        // write the real blob. A directory is left alone so the move below fails with its own error.
        RemoveUnusableBlob(target, data.Length);

        string temp = Path.Combine(
            directory,
            ".tmp-" + digest + "-" + Guid.NewGuid().ToString("N") + "." + extension
        );
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                stream.Write(data.Span);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temp, target);
            }
            catch (IOException) when (IsUsableBlob(target, data.Length))
            {
                // A concurrent write of the same bytes won the rename race. Content addressing
                // makes the winner's blob the same blob, so the reference is still valid.
                TryDelete(temp);
            }
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        return reference;
    }

    /// <summary>
    /// Reads the bytes a reference names, from the session's blobs or the in-memory store. A
    /// missing blob is false; the caller decides whether that is an error.
    /// </summary>
    internal bool TryRead(ImageContentReference reference, out ReadOnlyMemory<byte> data)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (_memory is not null)
        {
            lock (_gate)
            {
                if (_memory.TryGetValue(reference.Digest, out byte[]? stored))
                {
                    data = stored;
                    return true;
                }
            }

            data = default;
            return false;
        }

        lock (_gate)
        {
            if (_cache.TryGetValue(reference.Digest, out byte[]? cached))
            {
                data = cached;
                return true;
            }
        }

        if (!TryBlobPath(reference, out string path) || !File.Exists(path))
        {
            data = default;
            return false;
        }

        byte[] bytes;
        try
        {
            if (new FileInfo(path).LinkTarget is not null)
            {
                throw new InvalidDataException(
                    $"Session image blob '{path}' is a link, not a stored blob; delete it and re-attach the image."
                );
            }

            using var stream = SessionFiles.OpenRead(path);
            long length = stream.Length;
            if (length > ImageLimits.MaxBytes)
            {
                throw new InvalidDataException(
                    $"Session image blob '{path}' is {length} bytes, over the {ImageLimits.MaxBytes}-byte image limit; the blob is corrupt. Delete it and re-attach the image."
                );
            }

            bytes = new byte[length];
            stream.ReadExactly(bytes);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            data = default;
            return false;
        }

        lock (_gate)
        {
            if (
                !_cache.ContainsKey(reference.Digest)
                && _cacheBytes + bytes.Length <= CacheLimitBytes
            )
            {
                _cache[reference.Digest] = bytes;
                _cacheBytes += bytes.Length;
            }
        }

        data = bytes;
        return true;
    }

    private bool TryBlobPath(ImageContentReference reference, out string path)
    {
        if (!IsValidDigest(reference.Digest))
        {
            path = string.Empty;
            return false;
        }

        string? extension = ExtensionOrNull(reference.MimeType);
        if (extension is null)
        {
            path = string.Empty;
            return false;
        }

        path = Path.Combine(DirectoryPath!, BlobName(reference.Digest, extension));
        return true;
    }

    /// <summary>
    /// A blob already at the target is reusable only when it is a regular file of the expected
    /// length; a link, a truncated partial write or a directory is not.
    /// </summary>
    private static bool IsUsableBlob(string path, long expectedLength)
    {
        try
        {
            FileInfo info = new(path);
            return info.Exists && info.LinkTarget is null && info.Length == expectedLength;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Removes the blob at <paramref name="path"/> only when it is corrupt: a link, or a file whose
    /// length is not the one the caller is about to write. A file of the expected length is the blob
    /// a concurrent store of the same bytes completed between this store's check and this call —
    /// removing it would take the blob out from under that store's rename, whose <see cref="Store"/>
    /// then finds nothing at the target and rethrows instead of accepting the peer's blob.
    /// </summary>
    internal static void RemoveUnusableBlob(string path, long expectedLength)
    {
        try
        {
            FileInfo info = new(path);
            if (info.LinkTarget is null && (!info.Exists || info.Length == expectedLength))
            {
                return;
            }

            info.Delete();
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        { }
    }

    private static string BlobName(string digest, string extension) => digest + "." + extension;

    private static string ExtensionFor(string mimeType) =>
        ExtensionOrNull(mimeType)
        ?? throw new ArgumentException(
            $"Unsupported image media type '{mimeType}'; expected image/png, image/jpeg, image/gif or image/webp.",
            nameof(mimeType)
        );

    private static string? ExtensionOrNull(string mimeType) =>
        mimeType switch
        {
            "image/png" => "png",
            "image/jpeg" => "jpg",
            "image/gif" => "gif",
            "image/webp" => "webp",
            _ => null,
        };

    /// <summary>The absolute path a reference's blob is expected at; throws for an unsupported media type.</summary>
    internal string BlobPath(ImageContentReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (DirectoryPath is not { } directory)
        {
            throw new InvalidOperationException(
                "An in-memory image store has no blob path; it never writes images to disk."
            );
        }

        if (!IsValidDigest(reference.Digest))
        {
            throw new ArgumentException(
                $"Image digest '{reference.Digest}' is not a lowercase 64-character SHA-256 hex string.",
                nameof(reference)
            );
        }

        return Path.Combine(
            directory,
            BlobName(reference.Digest, ExtensionFor(reference.MimeType))
        );
    }

    /// <summary>
    /// Fails a resumed run before its first request when a referenced blob is absent, naming the
    /// digest and the expected path. An in-memory store never reports a missing image.
    /// </summary>
    internal void ValidateReferences(IEnumerable<ImageContentReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        if (IsInMemory)
        {
            return;
        }

        foreach (ImageContentReference reference in references)
        {
            ArgumentNullException.ThrowIfNull(reference);
            string path = BlobPath(reference);
            if (!File.Exists(path))
            {
                throw new InvalidDataException(
                    $"Session image '{reference.Digest}' is missing; expected the blob at '{path}'. Re-attach the image or start a new session."
                );
            }
        }
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
}
