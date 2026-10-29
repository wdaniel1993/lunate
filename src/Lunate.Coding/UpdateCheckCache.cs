using System.Globalization;
using System.Text.Json;

namespace Lunate.Coding;

/// <summary>
/// One day's answer from the releases endpoint. A reservation entry is written before the request
/// and carries null version and URL: a failed or crashed attempt still consumes the day, so the
/// check can never become a request storm. <see cref="IsFresh"/> treats a future stamp as fresh, so
/// clock skew does not cause a second request either.
/// </summary>
internal sealed record UpdateCheckCache(
    DateTimeOffset CheckedAt,
    string? LatestVersion,
    string? ChangelogUrl
)
{
    internal bool IsFresh(DateTimeOffset now) => now - CheckedAt < UpdateCheck.Lifetime;

    /// <summary>The usable answer, or null for a reservation entry or a partial answer.</summary>
    internal UpdateInfo? Info =>
        LatestVersion is { Length: > 0 } version && ChangelogUrl is { Length: > 0 } url
            ? new UpdateInfo(version, url)
            : null;

    /// <summary>Reads the cache; a missing, malformed or unreadable file is no cache at all.</summary>
    internal static UpdateCheckCache? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;
            if (
                root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("checkedAt", out JsonElement checkedAt)
                || checkedAt.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(
                    checkedAt.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset parsed
                )
            )
            {
                return null;
            }

            return new UpdateCheckCache(
                parsed,
                Text(root, "latestVersion"),
                Text(root, "changelogUrl")
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or NotSupportedException
                        or ArgumentException
            )
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the cache atomically: LF, UTF-8 without BOM, compact JSON and one trailing newline.
    /// A cache write is best effort; failing to write it never fails the session.
    /// </summary>
    internal static void Write(string path, UpdateCheckCache cache)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(temp, Serialize(cache));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or NotSupportedException
                        or ArgumentException
            )
        {
            // The check is an announcement, never a session prerequisite.
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
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static byte[] Serialize(UpdateCheckCache cache)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString(
                "checkedAt",
                cache.CheckedAt.ToString("O", CultureInfo.InvariantCulture)
            );
            writer.WriteString("latestVersion", cache.LatestVersion);
            writer.WriteString("changelogUrl", cache.ChangelogUrl);
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static string? Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
