using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

internal sealed partial class SessionImages
{
    /// <summary>Whether a digest is a lowercase 64-character SHA-256 hex string.</summary>
    internal static bool IsValidDigest(string digest)
    {
        if (digest.Length != 64)
        {
            return false;
        }

        foreach (char character in digest)
        {
            if (!IsLowerHex(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Every digest a session's message entries reference: typed image parts and the digests found
    /// anywhere inside a raw-preserved message JSON. Invalid digests are ignored.
    /// </summary>
    internal static HashSet<string> ReferencedDigests(IEnumerable<SessionEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        HashSet<string> digests = new(StringComparer.Ordinal);
        foreach (SessionEntry entry in entries)
        {
            if (entry is not SessionMessageEntry message)
            {
                continue;
            }

            foreach (AIContent content in message.Message.Contents)
            {
                if (content is ImageContentReference reference && IsValidDigest(reference.Digest))
                {
                    digests.Add(reference.Digest);
                }
            }

            if (message.RawMessageJson is { } raw)
            {
                AddRawDigests(raw, digests);
            }
        }

        return digests;
    }

    /// <summary>
    /// Removes this session's blobs that no entry references, plus temporary files left older than
    /// a day. Only exact blob names and the temp prefix are touched; other files stay. A missing
    /// directory or an in-memory store is a no-op.
    /// </summary>
    internal void Sweep(IReadOnlySet<string> referencedDigests, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(referencedDigests);
        if (DirectoryPath is not { } directory || !Directory.Exists(directory))
        {
            return;
        }

        DateTime staleTemp = now.UtcDateTime.AddDays(-1);
        foreach (string path in Directory.EnumerateFiles(directory))
        {
            string name = Path.GetFileName(path);
            if (IsBlobName(name))
            {
                if (!referencedDigests.Contains(name[..64]))
                {
                    TryDelete(path);
                }

                continue;
            }

            if (name.StartsWith(".tmp-", StringComparison.Ordinal))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < staleTemp)
                    {
                        TryDelete(path);
                    }
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static void AddRawDigests(string rawMessageJson, HashSet<string> digests)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawMessageJson);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            ScanForDigests(document.RootElement, digests);
        }
    }

    private static void ScanForDigests(JsonElement element, HashSet<string> digests)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (
                    element.TryGetProperty("$type", out JsonElement type)
                    && type.ValueKind == JsonValueKind.String
                    && type.GetString() == "image"
                    && element.TryGetProperty("digest", out JsonElement digest)
                    && digest.ValueKind == JsonValueKind.String
                    && digest.GetString() is { } value
                    && IsValidDigest(value)
                )
                {
                    digests.Add(value);
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    ScanForDigests(property.Value, digests);
                }

                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    ScanForDigests(item, digests);
                }

                break;
        }
    }

    private static bool IsBlobName(string name)
    {
        if (name.Length < 65 || name[64] != '.' || !IsValidDigest(name[..64]))
        {
            return false;
        }

        ReadOnlySpan<char> extension = name.AsSpan(65);
        return extension.SequenceEqual("png")
            || extension.SequenceEqual("jpg")
            || extension.SequenceEqual("gif")
            || extension.SequenceEqual("webp");
    }

    private static bool IsLowerHex(char character) =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f';
}
