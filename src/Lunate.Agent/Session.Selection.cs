namespace Lunate.Agent;

public sealed partial class Session
{
    /// <summary>
    /// The newest session whose header recorded <paramref name="cwd"/>, compared with the
    /// platform's path comparison. Walks the whole store (header reads only) so a moved worktree
    /// still resolves; a missing root or no match is null. Ordering is the picker's
    /// (<see cref="List"/>): last-modified, then created, then id, all descending.
    /// </summary>
    public static SessionSummary? FindNewest(
        string sessionsRoot,
        string cwd,
        StringComparison comparison
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(cwd);

        SessionSummary? newest = null;
        foreach (string path in EnumerateSessionFiles(sessionsRoot))
        {
            if (ReadHeader(path) is not { } header || !string.Equals(header.Cwd, cwd, comparison))
            {
                continue;
            }

            if (SummaryOf(path, header) is not { } summary)
            {
                continue;
            }

            SessionSummary candidate = WithTitle(summary);
            if (newest is null || CompareNewestFirst(candidate, newest) < 0)
            {
                newest = candidate;
            }
        }

        return newest;
    }

    /// <summary>
    /// Every session file whose header id equals <paramref name="id"/>, newest first. More than one
    /// match is an ambiguous id for the caller to report by path; an unknown id lists nothing.
    /// </summary>
    public static IReadOnlyList<SessionSummary> FindById(string sessionsRoot, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        List<SessionSummary> matches = [];
        foreach (string path in EnumerateSessionFiles(sessionsRoot))
        {
            if (
                ReadHeader(path) is { } header
                && string.Equals(header.Id, id, StringComparison.Ordinal)
                && SummaryOf(path, header) is { } summary
            )
            {
                matches.Add(WithTitle(summary));
            }
        }

        matches.Sort(CompareNewestFirst);
        return matches;
    }

    /// <summary>Every <c>.jsonl</c> under the root, recursively, never following unreadable paths.</summary>
    private static IEnumerable<string> EnumerateSessionFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (
            string path in Directory.EnumerateFiles(
                root,
                "*.jsonl",
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    // Hidden and system entries still list; a symlinked directory is not recursed
                    // into, so a self-referential store cannot walk forever.
                    AttributesToSkip = FileAttributes.ReparsePoint,
                }
            )
        )
        {
            yield return path;
        }
    }
}
