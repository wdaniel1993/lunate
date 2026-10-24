namespace Lunate.Agent;

/// <summary>One session file as the picker lists it: id, path, creation and last-write times.</summary>
public sealed record SessionSummary(
    string Id,
    string Path,
    DateTimeOffset Created,
    DateTimeOffset Modified
)
{
    /// <summary>The title from the sidecar beside the session file; null when it has none.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// Equality ignores <see cref="Title"/>: it is presentation data read from a sidecar, not part
    /// of the session's identity (id, path and the file's timestamps).
    /// </summary>
    public bool Equals(SessionSummary? other) =>
        other is not null
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && string.Equals(Path, other.Path, StringComparison.Ordinal)
        && Created.Equals(other.Created)
        && Modified.Equals(other.Modified);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(Path, StringComparer.Ordinal);
        hash.Add(Created);
        hash.Add(Modified);
        return hash.ToHashCode();
    }
};

public sealed partial class Session
{
    /// <summary>The most sessions a directory listing returns.</summary>
    internal const int ListLimit = 20;

    /// <summary>
    /// Lists a session directory for the picker: one entry per <c>.jsonl</c> file whose header
    /// reads, newest last-modified first with creation time and then the id as descending
    /// tie-breaks (so same-second sessions still order deterministically), bounded to
    /// <see cref="ListLimit"/>. Files are read as little as possible (the header line, and the
    /// title sidecar only for the rows the listing returns) and never modified; a missing
    /// directory lists nothing.
    /// </summary>
    public static IReadOnlyList<SessionSummary> List(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!System.IO.Directory.Exists(directory))
        {
            return [];
        }

        List<SessionSummary> summaries = [];
        foreach (string path in System.IO.Directory.EnumerateFiles(directory, "*.jsonl"))
        {
            if (ReadSummary(path) is { } summary)
            {
                summaries.Add(summary);
            }
        }

        summaries.Sort(CompareNewestFirst);
        int count = Math.Min(summaries.Count, ListLimit);
        for (var index = 0; index < count; index++)
        {
            summaries[index] = WithTitle(summaries[index]);
        }

        return summaries.GetRange(0, count);
    }

    /// <summary>The picker order and the launch-selection order: newest first, then id descending.</summary>
    private static int CompareNewestFirst(SessionSummary left, SessionSummary right)
    {
        int byModified = right.Modified.CompareTo(left.Modified);
        if (byModified != 0)
        {
            return byModified;
        }

        int byCreated = right.Created.CompareTo(left.Created);
        return byCreated != 0 ? byCreated : string.CompareOrdinal(right.Id, left.Id);
    }

    private static SessionSummary? ReadSummary(string path) =>
        ReadHeader(path) is { } header ? SummaryOf(path, header) : null;

    /// <summary>Reads the first line only; a missing, unreadable or corrupt header reads as null.</summary>
    private static SessionHeaderEntry? ReadHeader(string path)
    {
        try
        {
            using var reader = SessionFiles.OpenTextReader(path);
            if (reader.ReadLine() is not { } line)
            {
                return null;
            }

            return SessionFormat.Parse(line) as SessionHeaderEntry;
        }
        catch (Exception exception)
            when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static SessionSummary? SummaryOf(string path, SessionHeaderEntry header)
    {
        try
        {
            return new(
                header.Id,
                path,
                header.Created,
                new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Adds the title sidecar of a row the caller returns (never one past the list cap).</summary>
    private static SessionSummary WithTitle(SessionSummary summary) =>
        summary with
        {
            Title = SessionTitle.Read(summary.Path),
        };
}
