namespace LspDiagnosticsExtension;

/// <summary>
/// Resolves a tool call's path argument against the workspace root. Relative paths combine with
/// the root; the result must stay inside it. Windows separator differences are tolerated because
/// both sides are normalized by <see cref="Path.GetFullPath(string)"/>.
/// </summary>
internal static class WorkspacePath
{
    public static bool TryResolve(string worktreeRoot, string path, out string absolute)
    {
        absolute = string.Empty;
        if (string.IsNullOrWhiteSpace(worktreeRoot) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string root;
        string candidate;
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(worktreeRoot));
            candidate = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        }
        catch (Exception exception)
            when (exception
                    is ArgumentException
                        or IOException
                        or NotSupportedException
                        or PathTooLongException
            )
        {
            return false;
        }

        StringComparison comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        if (
            candidate.Equals(root, comparison)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison)
        )
        {
            absolute = candidate;
            return true;
        }

        return false;
    }
}
