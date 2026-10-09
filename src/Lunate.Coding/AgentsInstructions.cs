namespace Lunate.Coding;

/// <summary>
/// Composes the project instructions the agent appends to its system prompt: every
/// <c>AGENTS.md</c> found from the repository root (or the working directory when there is no
/// repository) down to the working directory, root first, each block headed by its relative path.
/// Missing files are skipped; when the root is not an ancestor of the working directory (a linked
/// worktree), only the working directory's file is read.
/// </summary>
public static class AgentsInstructions
{
    /// <summary>The instructions file name looked up in every directory on the chain.</summary>
    public const string FileName = "AGENTS.md";

    /// <summary>
    /// Returns the concatenated, path-headed instruction blocks, or an empty string when no
    /// <c>AGENTS.md</c> exists on the chain.
    /// </summary>
    public static string Compose(string root, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        string canonicalRoot = Workspace.Canonicalize(root);
        string canonicalWorking = Workspace.Canonicalize(workingDirectory);
        bool underRoot = IsAtOrUnder(canonicalRoot, canonicalWorking);
        IReadOnlyList<string> chain = underRoot
            ? Chain(canonicalRoot, canonicalWorking)
            : [canonicalWorking];

        var blocks = new List<string>();
        foreach (string directory in chain)
        {
            string path = Path.Combine(directory, FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            string relative = underRoot ? RelativePath(canonicalRoot, path) : FileName;
            blocks.Add($"### AGENTS.md ({relative})\n\n{File.ReadAllText(path).TrimEnd()}");
        }

        return string.Join("\n\n", blocks);
    }

    /// <summary>The ancestor chain from <paramref name="root"/> down to <paramref name="working"/>.</summary>
    private static IReadOnlyList<string> Chain(string root, string working)
    {
        var chain = new List<string>();
        for (
            string? current = working;
            current is not null;
            current = Path.GetDirectoryName(current)
        )
        {
            chain.Add(current);
            if (PathsEqual(current, root))
            {
                break;
            }
        }

        chain.Reverse();
        return chain;
    }

    private static bool IsAtOrUnder(string root, string path)
    {
        if (PathsEqual(root, path))
        {
            return true;
        }

        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, Comparison);
    }

    private static string RelativePath(string root, string path) =>
        Path.GetRelativePath(root, path)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

    private static bool PathsEqual(string left, string right) => left.Equals(right, Comparison);

    private static StringComparison Comparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}
