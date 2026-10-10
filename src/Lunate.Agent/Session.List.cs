namespace Lunate.Agent;

/// <summary>One session file as the picker lists it: id, path, creation and last-write times.</summary>
public sealed record SessionSummary(
    string Id,
    string Path,
    DateTimeOffset Created,
    DateTimeOffset Modified
);

public sealed partial class Session
{
    /// <summary>The most sessions a directory listing returns.</summary>
    internal const int ListLimit = 20;

    /// <summary>
    /// Lists a session directory for the picker: one entry per <c>.jsonl</c> file whose header
    /// reads, newest last-modified first with creation time and then the id as descending
    /// tie-breaks (so same-second sessions still order deterministically), bounded to
    /// <see cref="ListLimit"/>. Files are read as little as possible (the header line only) and
    /// never modified; a missing directory lists nothing.
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

        summaries.Sort(
            static (left, right) =>
            {
                int byModified = right.Modified.CompareTo(left.Modified);
                if (byModified != 0)
                {
                    return byModified;
                }

                int byCreated = right.Created.CompareTo(left.Created);
                return byCreated != 0 ? byCreated : string.CompareOrdinal(right.Id, left.Id);
            }
        );
        return summaries.Count <= ListLimit ? summaries : summaries.GetRange(0, ListLimit);
    }

    private static SessionSummary? ReadSummary(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            if (reader.ReadLine() is not { } line)
            {
                return null;
            }

            if (SessionFormat.Parse(line) is not SessionHeaderEntry header)
            {
                return null;
            }

            return new SessionSummary(
                header.Id,
                path,
                header.Created,
                new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)
            );
        }
        catch (Exception exception)
            when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
