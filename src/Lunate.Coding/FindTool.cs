using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// The optional <c>find</c> tool: glob matching over the workspace walk, files by default and
/// directories on request. Matching is case-sensitive on Linux and case-insensitive elsewhere,
/// following the workspace's own rule.
/// </summary>
public sealed class FindTool : ITool
{
    private const int DefaultMaxResults = 100;
    private const int MaxResultsCap = 500;

    private static readonly IReadOnlyList<string> Accepted =
    [
        "pattern",
        "path",
        "type",
        "max_results",
    ];

    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "pattern": {
                  "type": "string",
                  "description": "Glob matched against paths relative to path, for example '*.cs' or 'src/**/*.cs'"
                },
                "path": {
                  "type": "string",
                  "description": "Directory to search, relative to the workspace (default the workspace root)"
                },
                "type": {
                  "type": "string",
                  "enum": ["file", "dir"],
                  "description": "'file' (default) or 'dir'"
                },
                "max_results": {
                  "type": "integer",
                  "description": "Maximum results (default 100, max 500)"
                }
              },
              "required": ["pattern"]
            }
            """
        )
        .RootElement.Clone();

    private readonly Workspace _workspace;

    public FindTool(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
    }

    public string Name => "find";

    public string Description =>
        "Find files (or directories with type 'dir') by glob, relative to the workspace. "
        + "Honours .gitignore, sorts the paths and stops at 100 results by default (max 500) "
        + "with '[N more files]'.";

    public JsonElement ParametersSchema => Schema;

    public ToolRisk Risk => ToolRisk.ReadOnly;

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        if (!ToolArguments.TryValidate(args, Accepted, out string? argumentError))
        {
            return Task.FromResult(Error(argumentError!));
        }

        if (
            !ToolArguments.TryString(args, "pattern", out string? pattern, out string? error)
            || !ToolArguments.TryString(args, "path", out string? path, out error)
            || !ToolArguments.TryString(args, "type", out string? type, out error)
            || !ToolArguments.TryInteger(args, "max_results", out int? maxResults, out error)
        )
        {
            return Task.FromResult(Error(error!));
        }

        if (string.IsNullOrEmpty(pattern))
        {
            return Task.FromResult(Error("pattern is required and must be a non-empty string"));
        }

        string kind = type ?? "file";
        if (kind is not ("file" or "dir"))
        {
            return Task.FromResult(Error("type must be 'file' or 'dir'"));
        }

        var limit = maxResults ?? DefaultMaxResults;
        if (limit < 1)
        {
            return Task.FromResult(Error("max_results must be >= 1"));
        }

        limit = Math.Min(limit, MaxResultsCap);

        string requested = path ?? ".";
        if (!_workspace.TryResolve(requested, out ResolvedPath resolved, out string? pathError))
        {
            return Task.FromResult(Error(pathError));
        }

        bool isDirectory = Directory.Exists(resolved.AbsolutePath);
        bool isFile = File.Exists(resolved.AbsolutePath);
        if (!isDirectory && !isFile)
        {
            return Task.FromResult(Error($"path not found: {resolved.RelativePath}"));
        }

        GlobPattern glob;
        try
        {
            glob = GlobPattern.Parse(pattern);
        }
        catch (ArgumentException exception)
        {
            return Task.FromResult(
                Error(
                    $"pattern is not a valid glob: {exception.Message}; use a pattern like '*.cs'"
                )
            );
        }

        bool wantDirectories = kind == "dir";
        string scope = resolved.RelativePath is "." or "" ? string.Empty : resolved.RelativePath;
        List<string> matches = [];
        try
        {
            if (isFile)
            {
                string name = Path.GetFileName(resolved.RelativePath);
                if (!wantDirectories && glob.IsMatch(name))
                {
                    matches.Add(resolved.RelativePath);
                }
            }
            else
            {
                IReadOnlyList<WorkspaceEntry> visible = FileIndex.VisibleEntries(
                    new SystemWorkspaceFiles(_workspace.WorktreeRoot)
                );
                foreach (WorkspaceEntry entry in visible)
                {
                    ct.ThrowIfCancellationRequested();
                    if (
                        entry.IsDirectory != wantDirectories
                        || !SearchScope.Contains(scope, entry.Path)
                    )
                    {
                        continue;
                    }

                    string candidate =
                        scope.Length == 0 ? entry.Path : entry.Path[(scope.Length + 1)..];
                    if (!glob.IsMatch(candidate))
                    {
                        continue;
                    }

                    matches.Add(entry.IsDirectory ? entry.Path + "/" : entry.Path);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(
                Error($"could not search '{resolved.RelativePath}': {exception.Message}")
            );
        }

        List<string> shown = matches.Take(limit).ToList();
        if (shown.Count == 0)
        {
            string noun = wantDirectories ? "directories" : "files";
            return Task.FromResult(
                Result($"No {noun} matching '{pattern}' in {resolved.RelativePath}")
            );
        }

        var output = new StringBuilder(string.Join('\n', shown));
        int more = matches.Count - shown.Count;
        if (more > 0)
        {
            output
                .Append('\n')
                .Append(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"[{more} more {(more == 1 ? "file" : "files")}]"
                    )
                );
        }

        return Task.FromResult(Result(output.ToString()));
    }

    private static ToolResult Result(string output) => new(output, IsError: false);

    private static ToolResult Error(string output) => new(output, IsError: true);
}
