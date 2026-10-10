namespace Lunate.Coding;

/// <summary>One entry of the workspace walk: a relative path (forward slashes) and its kind.</summary>
internal sealed record WorkspaceEntry(string Path, bool IsDirectory);

/// <summary>
/// The filesystem seam behind the file index: a recursive enumeration of the workspace
/// (files and directories, relative paths) and on-demand reads of individual files. Tests
/// substitute an in-memory implementation so ignore semantics and bounds are deterministic.
/// </summary>
internal interface IWorkspaceFiles
{
    /// <summary>All entries under the workspace root; `.git` and ignored paths are the index's job.</summary>
    IReadOnlyList<WorkspaceEntry> Enumerate();

    /// <summary>The file's text, or null when it does not exist or cannot be read.</summary>
    string? ReadAllText(string relativePath);
}

/// <summary>
/// The production walk: recursive over the workspace root, relative forward-slash paths for files
/// and directories, directory symlinks not followed and not listed, `.git` skipped, unreadable
/// directories skipped (best effort, never throwing).
/// </summary>
internal sealed class SystemWorkspaceFiles : IWorkspaceFiles
{
    private readonly string _workspaceRoot;

    public SystemWorkspaceFiles(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = workspaceRoot;
    }

    public IReadOnlyList<WorkspaceEntry> Enumerate()
    {
        List<WorkspaceEntry> entries = [];
        Walk(_workspaceRoot, string.Empty, entries);
        return entries;
    }

    public string? ReadAllText(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        try
        {
            return File.ReadAllText(Path.Combine(_workspaceRoot, ToSystemPath(relativePath)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Walk(string absolute, string relative, List<WorkspaceEntry> entries)
    {
        FileSystemInfo[] children;
        try
        {
            children = new DirectoryInfo(absolute).GetFileSystemInfos();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (FileSystemInfo child in children)
        {
            if (string.Equals(child.Name, ".git", StringComparison.Ordinal))
            {
                continue;
            }

            string childRelative = relative.Length == 0 ? child.Name : relative + "/" + child.Name;
            if (child is not DirectoryInfo directory)
            {
                entries.Add(new WorkspaceEntry(childRelative, IsDirectory: false));
                continue;
            }

            if (directory.LinkTarget is null)
            {
                entries.Add(new WorkspaceEntry(childRelative, IsDirectory: true));
                Walk(child.FullName, childRelative, entries);
            }
        }
    }

    private static string ToSystemPath(string relativePath) =>
        relativePath.Replace('/', Path.DirectorySeparatorChar);
}
