using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>Reads text files inside the workspace with numbered lines and a continuation footer.</summary>
public sealed class ReadTool(Workspace workspace, ITextFileAccess? files = null) : ITool
{
    private const int DefaultLimit = 2000;
    private const int MaxLimit = 2000;
    private const int ProbeBytes = 8192;

    private readonly ITextFileAccess _files = files ?? LocalTextFileAccess.Instance;

    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Path of the file to read, relative to the workspace or absolute inside it"
                },
                "offset": { "type": "integer", "description": "1-based first line (default 1)" },
                "limit": { "type": "integer", "description": "maximum lines (default 2000, max 2000)" }
              },
              "required": ["path"]
            }
            """
        )
        .RootElement.Clone();

    public string Name => "read";

    public string Description =>
        "Read a text file from the workspace as numbered lines; use offset and limit to page.";

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
        if (args.TryGetProperty("offset", out var offsetElement))
        {
            if (!TryReadInteger(offsetElement, out offset))
            {
                return Task.FromResult(Error("offset must be an integer"));
            }
        }

        var limit = DefaultLimit;
        if (args.TryGetProperty("limit", out var limitElement))
        {
            if (!TryReadInteger(limitElement, out limit))
            {
                return Task.FromResult(Error("limit must be an integer"));
            }
        }

        if (!workspace.TryResolve(pathElement.GetString()!, out var resolved, out var error))
        {
            return Task.FromResult(Error(error));
        }

        if (Directory.Exists(resolved.AbsolutePath))
        {
            return Task.FromResult(Error($"{resolved.RelativePath} is a directory; use bash ls"));
        }

        if (!_files.Exists(resolved.AbsolutePath))
        {
            return Task.FromResult(Error($"file not found: {resolved.RelativePath}"));
        }

        string text;
        bool hasBom;
        (string Text, long Length)? probe;
        try
        {
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

        if (offset < 1)
        {
            return Task.FromResult(Error("offset must be >= 1"));
        }

        if (limit < 1)
        {
            return Task.FromResult(Error("limit must be >= 1"));
        }

        limit = Math.Min(limit, MaxLimit);

        string[] lines = TextFile.SplitLines(text);

        var total = lines.Length;
        if (total == 0)
        {
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
