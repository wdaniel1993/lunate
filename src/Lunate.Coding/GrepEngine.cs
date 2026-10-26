namespace Lunate.Coding;

/// <summary>One grep request: the search root is already resolved and inside the workspace.</summary>
internal sealed record GrepRequest(
    string Pattern,
    string Root,
    string RelativeRoot,
    bool SearchIsFile,
    string? Glob,
    bool CaseSensitive,
    int MaxResults
);

/// <summary>One matching line: a workspace-relative path, its 1-based line and the line's text.</summary>
internal sealed record GrepMatch(string Path, int Line, string Text);

/// <summary>
/// The result of one engine run: the first <see cref="MaxResults"/> matches in path/line order,
/// how many further matches the cap cut, and an engine error (invalid pattern, timeout) when the
/// search could not run.
/// </summary>
internal sealed record GrepSearch(IReadOnlyList<GrepMatch> Matches, int More, string? Error);

/// <summary>
/// The two grep engines behind <see cref="GrepTool"/>: the installed ripgrep binary, or the
/// in-process walker. Both honour `.gitignore`, skip binary files and report the same shape.
/// </summary>
internal interface IGrepEngine
{
    Task<GrepSearch> SearchAsync(GrepRequest request, CancellationToken ct);
}

/// <summary>
/// The workspace containment both engines apply to every file they are about to read: the root is
/// canonicalized once per search, and each candidate's canonical path must equal it or lie under
/// it. A symlink — or a file swapped for one after the walk — therefore cannot make the tool read
/// bytes from outside the workspace, whichever engine runs.
/// </summary>
internal sealed class GrepScope
{
    private readonly string _root;

    private GrepScope(string root) => _root = root;

    /// <summary>The scope, or null when the root itself cannot be canonicalized (fail closed).</summary>
    internal static GrepScope? Create(string root) =>
        TryCanonicalize(root, out string canonical) ? new GrepScope(canonical) : null;

    /// <summary>True when <paramref name="absolute"/> is a file this search is allowed to read.</summary>
    internal bool Contains(string absolute) =>
        TryCanonicalize(absolute, out string canonical) && Workspace.IsInside(_root, canonical);

    private static bool TryCanonicalize(string path, out string canonical)
    {
        try
        {
            canonical = Workspace.Canonicalize(path);
            return true;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            canonical = string.Empty;
            return false;
        }
    }
}
