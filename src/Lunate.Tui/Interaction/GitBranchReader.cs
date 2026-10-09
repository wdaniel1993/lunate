namespace Lunate.Tui;

/// <summary>
/// Reads the current branch from <c>.git</c> by file access only — no process is spawned, because a
/// branch read can happen per frame. Handles a normal repository, a worktree <c>.git</c> file
/// (<c>gitdir:</c>, relative or absolute), and a detached HEAD (first seven SHA characters). Any
/// failure degrades to no branch.
/// </summary>
public static class GitBranchReader
{
    private const string RefPrefix = "ref: ";
    private const string HeadsPrefix = "refs/heads/";
    private const string GitDirPrefix = "gitdir:";
    private const int ShortShaLength = 7;

    public static string? Read(string workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory))
        {
            return null;
        }

        try
        {
            string gitPath = Path.Combine(workingDirectory, ".git");
            string? headPath =
                Directory.Exists(gitPath) ? Path.Combine(gitPath, "HEAD")
                : File.Exists(gitPath) ? HeadPathFromGitFile(gitPath, workingDirectory)
                : null;

            return headPath is null ? null : ParseHead(File.ReadAllText(headPath));
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string? HeadPathFromGitFile(string gitFile, string workingDirectory)
    {
        string? line = FirstLine(File.ReadAllText(gitFile));
        if (line is null || !line.StartsWith(GitDirPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string target = line[GitDirPrefix.Length..].Trim();
        if (target.Length == 0)
        {
            return null;
        }

        string gitDir = Path.IsPathRooted(target) ? target : Path.Combine(workingDirectory, target);
        return Path.Combine(gitDir, "HEAD");
    }

    private static string? ParseHead(string content)
    {
        string? line = FirstLine(content);
        if (line is null)
        {
            return null;
        }

        if (line.StartsWith(RefPrefix, StringComparison.Ordinal))
        {
            string reference = line[RefPrefix.Length..].Trim();
            if (reference.Length == 0)
            {
                return null;
            }

            return reference.StartsWith(HeadsPrefix, StringComparison.Ordinal)
                ? reference[HeadsPrefix.Length..]
                : reference;
        }

        return line.Length > ShortShaLength ? line[..ShortShaLength] : line;
    }

    private static string? FirstLine(string content)
    {
        int index = content.IndexOf('\n');
        string line = (index < 0 ? content : content[..index]).Trim();
        return line.Length == 0 ? null : line;
    }
}
