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
