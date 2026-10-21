using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>
/// The optional <c>ls</c> tool: one directory listing (depth 1) or a bounded tree (depth 3 max).
/// Directories come first, files carry their byte size, hidden entries are included and
/// <c>.git</c> is skipped at every depth.
/// </summary>
public sealed class LsTool : ITool
{
    private const int DefaultDepth = 1;
    private const int MaxDepth = 3;
    private const int DefaultMaxEntries = 500;
    private const int MaxEntriesCap = 2_000;

    private static readonly IReadOnlyList<string> Accepted = ["path", "depth", "max_entries"];

    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Directory to list, relative to the workspace (default the workspace root)"
                },
                "depth": {
                  "type": "integer",
                  "description": "Tree depth (default 1, max 3)"
                },
                "max_entries": {
                  "type": "integer",
                  "description": "Maximum entries rendered (default 500, max 2000)"
                }
              }
            }
            """
        )
        .RootElement.Clone();

    private readonly Workspace _workspace;

    public LsTool(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
    }

    public string Name => "ls";

    public string Description =>
        "List one directory (depth 1) or a tree up to depth 3, directories first then files "
        + "with their byte size. Hidden entries are included, .git is always skipped, and a cap "
        + "ends the listing with '[N more entries]'.";

    public JsonElement ParametersSchema => Schema;

    public ToolRisk Risk => ToolRisk.ReadOnly;

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        if (!ToolArguments.TryValidate(args, Accepted, out string? argumentError))
        {
            return Task.FromResult(Error(argumentError!));
        }

        if (
            !ToolArguments.TryString(args, "path", out string? path, out string? error)
            || !ToolArguments.TryInteger(args, "depth", out int? depth, out error)
            || !ToolArguments.TryInteger(args, "max_entries", out int? maxEntries, out error)
        )
        {
            return Task.FromResult(Error(error!));
        }

        int levels = depth ?? DefaultDepth;
        if (levels < 1)
        {
            return Task.FromResult(Error("depth must be >= 1"));
        }

        levels = Math.Min(levels, MaxDepth);

        int limit = maxEntries ?? DefaultMaxEntries;
        if (limit < 1)
        {
            return Task.FromResult(Error("max_entries must be >= 1"));
        }

        limit = Math.Min(limit, MaxEntriesCap);

        string requested = path ?? ".";
        if (!_workspace.TryResolve(requested, out ResolvedPath resolved, out string? pathError))
        {
            return Task.FromResult(Error(pathError));
        }

        if (File.Exists(resolved.AbsolutePath))
        {
            return Task.FromResult(
                Error($"{resolved.RelativePath} is a file; ls lists directories")
            );
        }

        if (!Directory.Exists(resolved.AbsolutePath))
        {
            return Task.FromResult(Error($"path not found: {resolved.RelativePath}"));
        }

        string root = resolved.RelativePath is "." or "" ? string.Empty : resolved.RelativePath;
        if (
            !TryBuildChildren(
                root,
                out Dictionary<string, List<WorkspaceEntry>> children,
                out string? searchError
            )
        )
        {
            return Task.FromResult(Error(searchError!));
        }

        var output = new StringBuilder();
        var rendered = 0;
        var more = 0;
        Render(root, 1);

        if (output.Length == 0)
        {
            return Task.FromResult(Result("[empty directory]"));
        }

        if (more > 0)
        {
            output
                .Append('\n')
                .Append(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"[{more} more {(more == 1 ? "entry" : "entries")}]"
                    )
                );
        }

        return Task.FromResult(Result(output.ToString()));

        void Render(string parent, int level)
        {
            if (!children.TryGetValue(parent, out List<WorkspaceEntry>? entries))
            {
                return;
            }

            foreach (WorkspaceEntry entry in Sort(entries))
            {
                string name = entry.Path[(entry.Path.LastIndexOf('/') + 1)..];
                if (rendered < limit)
                {
                    if (output.Length > 0)
                    {
                        output.Append('\n');
                    }

                    output.Append(' ', (level - 1) * 2).Append(name);
                    if (entry.IsDirectory)
                    {
                        output.Append('/');
                    }
                    else
                    {
                        output
                            .Append("  ")
                            .Append(Size(entry.Path).ToString(CultureInfo.InvariantCulture));
                    }

                    rendered++;
                }
                else
                {
                    more++;
                }

                if (entry.IsDirectory && level < levels)
                {
                    Render(entry.Path, level + 1);
                }
            }
        }
    }

    private bool TryBuildChildren(
        string root,
        out Dictionary<string, List<WorkspaceEntry>> children,
        out string? error
    )
    {
        children = new Dictionary<string, List<WorkspaceEntry>>(StringComparer.Ordinal);
        error = null;
        try
        {
            IReadOnlyList<WorkspaceEntry> visible = FileIndex.VisibleEntries(
                new SystemWorkspaceFiles(_workspace.WorktreeRoot)
            );
            foreach (WorkspaceEntry entry in visible)
            {
                string parent = Parent(entry.Path);
                if (!children.TryGetValue(parent, out List<WorkspaceEntry>? list))
                {
                    list = [];
                    children[parent] = list;
                }

                list.Add(entry);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"could not list '{root}': {exception.Message}";
            return false;
        }
    }

    private static IEnumerable<WorkspaceEntry> Sort(List<WorkspaceEntry> entries) =>
        entries
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Path, StringComparer.Ordinal);

    private static string Parent(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    private long Size(string relativePath)
    {
        try
        {
            return new FileInfo(
                Path.Combine(
                    _workspace.WorktreeRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)
                )
            ).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static ToolResult Result(string output) => new(output, IsError: false);

    private static ToolResult Error(string output) => new(output, IsError: true);
}
