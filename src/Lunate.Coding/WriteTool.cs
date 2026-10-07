using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>Writes text files inside the workspace, creating parent directories as needed.</summary>
public sealed class WriteTool(Workspace workspace) : ITool
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Path of the file to write, relative to the workspace or absolute inside it"
                },
                "content": { "type": "string", "description": "The exact file content to write" }
              },
              "required": ["path", "content"]
            }
            """
        )
        .RootElement.Clone();

    public string Name => "write";

    public string Description =>
        "Write a text file into the workspace; creates parent directories and replaces existing files exactly.";

    public JsonElement ParametersSchema => Schema;

    public ToolRisk Risk => ToolRisk.Write;

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

        if (
            !args.TryGetProperty("content", out var contentElement)
            || contentElement.ValueKind != JsonValueKind.String
        )
        {
            return Task.FromResult(Error("content is required and must be a string"));
        }

        var content = contentElement.GetString()!;
        if (!workspace.TryResolve(pathElement.GetString()!, out var resolved, out var error))
        {
            return Task.FromResult(Error(error));
        }

        if (Directory.Exists(resolved.AbsolutePath))
        {
            return Task.FromResult(Error($"{resolved.RelativePath} is a directory"));
        }

        var created = !File.Exists(resolved.AbsolutePath);
        var oldText = created ? string.Empty : TextFile.ReadAllText(resolved.AbsolutePath);
        Directory.CreateDirectory(Path.GetDirectoryName(resolved.AbsolutePath)!);
        File.WriteAllText(resolved.AbsolutePath, content, Utf8NoBom);

        var lines = CountLines(content);
        var details = new WriteDetails(
            resolved.RelativePath,
            created,
            lines,
            LineDiff.Unified(oldText, content, resolved.RelativePath)
        );
        var output = string.Create(
            CultureInfo.InvariantCulture,
            $"wrote {lines} lines to {resolved.RelativePath} ({(created ? "created" : "replaced")})"
        );

        return Task.FromResult(new ToolResult(output, IsError: false, details));
    }

    private static int CountLines(string content)
    {
        if (content.Length == 0)
        {
            return 0;
        }

        var count = 0;
        foreach (var character in content)
        {
            if (character == '\n')
            {
                count++;
            }
        }

        return content.EndsWith('\n') ? count : count + 1;
    }

    private static ToolResult Error(string output) => new(output, IsError: true);
}
