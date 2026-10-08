namespace Lunate.Roslyn;

/// <summary>
/// The discovery rule for a worktree root (ADR-0006 / design): the solution in the root or one
/// level down; several candidates are resolved by the root's directory name, else refused.
/// </summary>
internal static class SolutionDiscovery
{
    public static SolutionDiscoveryResult Discover(string root)
    {
        if (!Directory.Exists(root))
        {
            return new SolutionDiscoveryResult(
                null,
                $"no solution found: '{root}' is not an existing directory"
            );
        }

        var candidates = new List<string>();
        AddCandidates(root, candidates);

        foreach (var directory in EnumerateDirectories(root))
        {
            var name = Path.GetFileName(directory);
            if (name is "bin" or "obj")
            {
                continue;
            }

            AddCandidates(directory, candidates);
        }

        candidates.Sort(PathIdentity.Comparer);

        if (candidates.Count == 0)
        {
            return new SolutionDiscoveryResult(
                null,
                $"no solution found: no .sln or .slnx under '{root}' or its immediate subdirectories"
            );
        }

        if (candidates.Count == 1)
        {
            return new SolutionDiscoveryResult(candidates[0], string.Empty);
        }

        var rootName = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)));
        var named = candidates
            .Where(candidate =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(candidate),
                    rootName,
                    PathIdentity.Comparison
                )
            )
            .ToList();

        if (named.Count == 1)
        {
            return new SolutionDiscoveryResult(named[0], string.Empty);
        }

        var listed = string.Join(", ", candidates.Select(Path.GetFileName));
        return new SolutionDiscoveryResult(
            null,
            $"several solutions found ({listed}); name the solution after the directory '{rootName}' or keep only one"
        );
    }

    private static IEnumerable<string> EnumerateDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void AddCandidates(string directory, List<string> candidates)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (Path.GetExtension(file) is ".sln" or ".slnx")
                {
                    candidates.Add(file);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreadable directory contributes no candidates.
        }
    }
}

internal readonly record struct SolutionDiscoveryResult(string? SolutionPath, string Message);
