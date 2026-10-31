using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>Reads text files inside the workspace with numbered lines and a continuation footer.
/// A per-run skill allowance (the discovered skill directories) grants read-only access to skill
/// files outside the workspace boundary, bundled files included (ADR-0016).</summary>
public sealed partial class ReadTool : ITool
{
    private const int DefaultLimit = 2000;
    private const int MaxLimit = 2000;
    private const int ProbeBytes = 8192;

    private readonly Workspace _workspace;
    private readonly ITextFileAccess _files;
    private readonly SkillReadAllowance? _allowance;

    public ReadTool(Workspace workspace, ITextFileAccess? files = null)
        : this(workspace, files, allowance: null) { }

    internal ReadTool(Workspace workspace, ITextFileAccess? files, SkillReadAllowance? allowance)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
        _files = files ?? LocalTextFileAccess.Instance;
        _allowance = allowance;
    }

    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Path of the file to read, relative to the workspace or absolute inside it; supported images (PNG, JPEG, GIF or WebP) are returned for a vision model"
                },
                "offset": { "type": "integer", "description": "1-based first line for workspace files; use the returned 0-based UTF-16 offset for spill pages" },
                "limit": { "type": "integer", "description": "maximum lines for workspace files (default 2000, max 2000); spill pages use the token budget" }
              },
              "required": ["path"]
            }
            """
        )
        .RootElement.Clone();

    public string Name => "read";

    public string Description =>
        "Read a text file from the workspace as numbered lines; use offset and limit to page. "
        + "Spill files return bounded exact-text pages with a continuation offset. "
        + "Supported images (PNG, JPEG, GIF or WebP) are returned for a vision model.";

    public JsonElement ParametersSchema => Schema;

    public ToolRisk Risk => ToolRisk.ReadOnly;

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        if (args.ValueKind != JsonValueKind.Object)
        {
            return Task.FromResult(Error("arguments must be a JSON object"));
        }

        if (
            !args.TryGetProperty("path", out var pathElement)
            || pathElement.ValueKind != JsonValueKind.String
        )
        {
            return Task.FromResult(Error("path is required and must be a string"));
        }

        var offset = 1;
        var hasOffset = false;
        if (args.TryGetProperty("offset", out var offsetElement))
        {
            hasOffset = true;
            if (!TryReadInteger(offsetElement, out offset))
            {
                return Task.FromResult(Error("offset must be an integer"));
            }
        }

        var limit = DefaultLimit;
        var hasLimit = false;
        if (args.TryGetProperty("limit", out var limitElement))
        {
            hasLimit = true;
            if (!TryReadInteger(limitElement, out limit))
            {
                return Task.FromResult(Error("limit must be an integer"));
            }
        }

        Session? session = AgentToolContextState.SessionFor(ctx);
        IReadLedger? reads = AgentToolContextState.ReadsFor(ctx);

        if (!_workspace.TryResolve(pathElement.GetString()!, out var resolved, out var error))
        {
            if (!TryResolveAllowed(pathElement.GetString()!, session, out resolved))
            {
                return Task.FromResult(Error(error));
            }
        }

        bool isKnownSpill =
            session is not null && ToolOutputSpill.IsKnownFile(session, resolved.AbsolutePath);

        if (Directory.Exists(resolved.AbsolutePath))
        {
            return Task.FromResult(Error($"{resolved.RelativePath} is a directory; use bash ls"));
        }

        string text;
        bool hasBom;
        (string Text, long Length)? probe;
        try
        {
            if (!_files.Exists(resolved.AbsolutePath))
            {
                return Task.FromResult(Error($"file not found: {resolved.RelativePath}"));
            }

            if (_files is IBinaryFileAccess binary)
            {
                var prefix = binary.ReadPrefixBytes(resolved.AbsolutePath, ImageFile.ProbeBytes);
                if (ImageFile.TryDetectMime(prefix, out _))
                {
                    return Task.FromResult(ReadImage(resolved, binary, ctx, hasOffset, hasLimit));
                }
            }

            probe = _files.ReadPrefix(resolved.AbsolutePath, ProbeBytes);
            if (probe is { } bounded && bounded.Text.Contains('\0'))
            {
                return Task.FromResult(
                    Error(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{resolved.RelativePath} is a binary file ({bounded.Length} bytes); read handles text files"
                        )
                    )
                );
            }

            (text, hasBom) = _files.ReadRaw(resolved.AbsolutePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Error($"could not be read: {exception.Message}"));
        }

        if (probe is null && text.Contains('\0'))
        {
            var size = Encoding.UTF8.GetByteCount(text) + (hasBom ? 3 : 0);
            return Task.FromResult(
                Error(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{resolved.RelativePath} is a binary file ({size} bytes); read handles text files"
                    )
                )
            );
        }

        if (isKnownSpill ? offset < 0 : offset < 1)
        {
            return Task.FromResult(
                Error(isKnownSpill ? "spill offset must be >= 0" : "offset must be >= 1")
            );
        }

        if (limit < 1)
        {
            return Task.FromResult(Error("limit must be >= 1"));
        }

        limit = Math.Min(limit, MaxLimit);

        if (isKnownSpill)
        {
            int spillOffset = hasOffset ? offset : 0;
            if (spillOffset > text.Length)
            {
                return Task.FromResult(
                    Error(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"offset {spillOffset} is past the end of spill output ({text.Length} characters)"
                        )
                    )
                );
            }

            reads?.Record(
                resolved.AbsolutePath,
                text,
                ctx.CallId ?? string.Empty,
                resolved.RelativePath
            );
            return Task.FromResult(
                ReadSpillPage(text, spillOffset, ctx.SpillThresholdTokens, ctx.ToolOutputLimit)
            );
        }

        string[] lines = TextFile.SplitLines(text);

        var total = lines.Length;
        if (total == 0)
        {
            reads?.Record(
                resolved.AbsolutePath,
                text,
                ctx.CallId ?? string.Empty,
                resolved.RelativePath
            );
            return Task.FromResult(Result("[empty file]"));
        }

        if (offset > total)
        {
            return Task.FromResult(
                Error(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"offset {offset} is past the end of {resolved.RelativePath} ({total} lines)"
                    )
                )
            );
        }

        var first = offset;
        var last = (int)Math.Min(offset + (long)limit - 1, total);
        var width = Math.Max(6, total.ToString(CultureInfo.InvariantCulture).Length);
        var output = new StringBuilder();
        for (var number = first; number <= last; number++)
        {
            if (output.Length > 0)
            {
                output.Append('\n');
            }

            output
                .Append(number.ToString(CultureInfo.InvariantCulture).PadLeft(width))
                .Append('|')
                .Append(lines[number - 1]);
        }

        if (last < total)
        {
            output.Append('\n');
            output.Append(
                CultureInfo.InvariantCulture,
                $"[lines {first}\u2013{last} of {total}, use offset to continue]"
            );
        }

        reads?.Record(
            resolved.AbsolutePath,
            text,
            ctx.CallId ?? string.Empty,
            resolved.RelativePath
        );
        return Task.FromResult(Result(output.ToString()));
    }

    private static bool TryReadInteger(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }

    private static ToolResult Result(string output) => new(output, IsError: false);

    private static ToolResult Error(string output) => new(output, IsError: true);
}
