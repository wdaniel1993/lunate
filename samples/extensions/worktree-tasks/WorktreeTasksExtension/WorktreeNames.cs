namespace WorktreeTasksExtension;

/// <summary>The worktree naming policy: names are sanitized before any git call and the derived
/// branch (<c>lunate/&lt;name&gt;</c>) is validated by <c>git check-ref-format --branch</c>.</summary>
internal static class WorktreeNames
{
    private const string AllowedCharacters =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-";

    /// <summary>The branch prefix every worktree branch carries.</summary>
    public const string BranchPrefix = "lunate/";

    /// <summary>
    /// A safe name: letters, digits, <c>.</c>, <c>-</c> and <c>_</c>; it starts alphanumeric, is at
    /// most 64 characters, contains no <c>..</c> and does not end in <c>.lock</c>. Path separators
    /// and escape sequences can never reach a branch or path this way.
    /// </summary>
    public static bool IsValid(string name) =>
        name.Length is > 0 and <= 64
        && char.IsAsciiLetterOrDigit(name[0])
        && !name.Contains("..", StringComparison.Ordinal)
        && !name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
        && name.All(character => AllowedCharacters.Contains(character));

    /// <summary>The worktree path: a sibling of the root named <c>&lt;rootName&gt;-&lt;name&gt;</c>.</summary>
    public static string TargetPath(string root, string name)
    {
        string full = Path.GetFullPath(root);
        return Path.Combine(Path.GetDirectoryName(full)!, Path.GetFileName(full) + "-" + name);
    }

    /// <summary>The branch for a name.</summary>
    public static string Branch(string name) => BranchPrefix + name;
}
