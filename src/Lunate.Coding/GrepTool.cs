using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// The optional <c>grep</c> tool: a bounded, read-only regex search over the workspace. It uses
/// the installed ripgrep binary when present and the in-process walker otherwise; both honour
/// <c>.gitignore</c>, skip binary files and stop at the cap.
/// </summary>
public sealed class GrepTool : ITool
{
    private const int DefaultMaxResults = 100;
    private const int MaxResultsCap = 500;

    private static readonly IReadOnlyList<string> Accepted =
    [
        "pattern",
        "path",
        "glob",
        "case_sensitive",
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
                  "description": ".NET regular expression matched against each line"
                },
                "path": {
                  "type": "string",
                  "description": "File or directory to search, relative to the workspace (default the workspace root)"
                },
                "glob": {
                  "type": "string",
                  "description": "Optional glob limiting the files searched, for example '*.cs'"
                },
                "case_sensitive": {
                  "type": "boolean",
                  "description": "Match case-sensitively (default true)"
                },
                "max_results": {
                  "type": "integer",
                  "description": "Maximum matching lines (default 100, max 500)"
                }
              },
              "required": ["pattern"]
            }
            """
        )
        .RootElement.Clone();

    private readonly Workspace _workspace;
    private readonly IGrepEngine _engine;

    public GrepTool(Workspace workspace)
        : this(workspace, RipgrepGrepEngine.TryCreate()) { }

    internal GrepTool(Workspace workspace, IGrepEngine? engine)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
        _engine = engine ?? new FallbackGrepEngine();
    }

    public string Name => "grep";

    public string Description =>
        "Search file contents with a .NET regular expression. Uses ripgrep when installed. "
        + "Honours .gitignore, skips binary files, returns 'path:line: text' sorted by path and "
        + "line, and stops at 100 matches by default (max 500) with '[N more matches]'.";

    public JsonElement ParametersSchema => Schema;

    public ToolRisk Risk => ToolRisk.ReadOnly;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement args,
        ToolContext ctx,
        CancellationToken ct
    )
    {
        if (!ToolArguments.TryValidate(args, Accepted, out string? argumentError))
        {
            return Error(argumentError!);
        }

        if (
            !ToolArguments.TryString(args, "pattern", out string? pattern, out string? error)
            || !ToolArguments.TryString(args, "path", out string? path, out error)
            || !ToolArguments.TryString(args, "glob", out string? glob, out error)
            || !ToolArguments.TryBoolean(args, "case_sensitive", out bool? caseSensitive, out error)
            || !ToolArguments.TryInteger(args, "max_results", out int? maxResults, out error)
        )
        {
            return Error(error!);
        }

        if (string.IsNullOrEmpty(pattern))
        {
            return Error("pattern is required and must be a non-empty string");
        }

        if (glob is { Length: 0 })
        {
            return Error("glob must be a non-empty string; omit it to search every file");
        }

        var limit = maxResults ?? DefaultMaxResults;
        if (limit < 1)
        {
            return Error("max_results must be >= 1");
        }

        limit = Math.Min(limit, MaxResultsCap);

        string requested = path ?? ".";
        if (!_workspace.TryResolve(requested, out ResolvedPath resolved, out string? pathError))
        {
            return Error(pathError);
        }

        bool isDirectory = Directory.Exists(resolved.AbsolutePath);
        bool isFile = File.Exists(resolved.AbsolutePath);
        if (!isDirectory && !isFile)
        {
            return Error($"path not found: {resolved.RelativePath}");
        }

        GrepSearch search = await _engine.SearchAsync(
            new GrepRequest(
                pattern,
                _workspace.WorktreeRoot,
                resolved.RelativePath,
                SearchIsFile: isFile,
                glob,
                CaseSensitive: caseSensitive ?? true,
                limit
            ),
            ct
        );
        if (search.Error is { } engineError)
        {
            return Error(engineError);
        }

        if (search.Matches.Count == 0 && search.More == 0)
        {
            string where = glob is null
                ? $"in {resolved.RelativePath}"
                : $"in {resolved.RelativePath} (glob '{glob}')";
            return Result($"No matches for '{pattern}' {where}");
        }

        var output = new StringBuilder();
        foreach (GrepMatch match in search.Matches)
        {
            if (output.Length > 0)
            {
                output.Append('\n');
            }

            output
                .Append(match.Path)
                .Append(':')
                .Append(match.Line.ToString(CultureInfo.InvariantCulture))
                .Append(": ")
                .Append(match.Text);
        }

        if (search.More > 0)
        {
            output
                .Append('\n')
                .Append(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"[{search.More} more {(search.More == 1 ? "match" : "matches")}]"
                    )
                );
        }

        return Result(output.ToString());
    }

    private static ToolResult Result(string output) => new(output, IsError: false);

    private static ToolResult Error(string output) => new(output, IsError: true);
}
