using System.Text.RegularExpressions;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// The in-process grep engine: the file index's walk (`.gitignore` chain, `.git` and directory
/// symlinks excluded), a NUL-byte binary probe, then one .NET-regex match per line. The result is
/// ordered exactly like ripgrep's <c>--sort path</c> output.
/// </summary>
internal sealed class FallbackGrepEngine(ITextFileAccess? files = null) : IGrepEngine
{
    private const int ProbeBytes = 8192;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    private readonly ITextFileAccess _files = files ?? LocalTextFileAccess.Instance;

    public Task<GrepSearch> SearchAsync(GrepRequest request, CancellationToken ct)
    {
        Regex regex;
        try
        {
            regex = new Regex(
                request.Pattern,
                RegexOptions.CultureInvariant
                    | (request.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase),
                MatchTimeout
            );
        }
        catch (ArgumentException exception)
        {
            return Task.FromResult(
                Error($"pattern is not a valid regular expression: {exception.Message}")
            );
        }

        GlobPattern? glob = null;
        if (request.Glob is { } pattern)
        {
            try
            {
                glob = GlobPattern.Parse(pattern);
            }
            catch (ArgumentException exception)
            {
                return Task.FromResult(
                    Error($"glob is not valid: {exception.Message}; use a pattern like '*.cs'")
                );
            }
        }

        IReadOnlyList<WorkspaceEntry> visible;
        try
        {
            visible = FileIndex.VisibleEntries(new SystemWorkspaceFiles(request.Root));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(
                Error($"could not search '{request.RelativeRoot}': {exception.Message}")
            );
        }

        List<GrepMatch> matches = [];
        var more = 0;
        try
        {
            foreach (WorkspaceEntry entry in visible)
            {
                ct.ThrowIfCancellationRequested();
                if (
                    entry.IsDirectory
                    || !InScope(entry.Path, request)
                    || glob is not null && !glob.IsMatch(entry.Path)
                )
                {
                    continue;
                }

                string absolute = Path.Combine(
                    request.Root,
                    entry.Path.Replace('/', Path.DirectorySeparatorChar)
                );
                if (IsBinary(absolute, out string text))
                {
                    continue;
                }

                string[] lines = TextFile.SplitLines(text);
                for (var index = 0; index < lines.Length; index++)
                {
                    if (!regex.IsMatch(lines[index]))
                    {
                        continue;
                    }

                    if (matches.Count < request.MaxResults)
                    {
                        matches.Add(new GrepMatch(entry.Path, index + 1, lines[index]));
                    }
                    else
                    {
                        more++;
                    }
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return Task.FromResult(
                Error($"pattern timed out after {MatchTimeout.TotalSeconds:0} s; simplify it")
            );
        }

        return Task.FromResult(new GrepSearch(matches, more, Error: null));
    }

    private bool IsBinary(string absolute, out string text)
    {
        text = string.Empty;
        try
        {
            var probe = _files.ReadPrefix(absolute, ProbeBytes);
            if (probe is { } bounded && bounded.Text.Contains('\0'))
            {
                return true;
            }

            text = _files.ReadAllText(absolute);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }

        return text.Contains('\0');
    }

    private static bool InScope(string path, GrepRequest request)
    {
        if (request.SearchIsFile)
        {
            return string.Equals(path, request.RelativeRoot, StringComparison.Ordinal);
        }

        return SearchScope.Contains(request.RelativeRoot, path);
    }

    private static GrepSearch Error(string message) => new([], 0, message);
}
