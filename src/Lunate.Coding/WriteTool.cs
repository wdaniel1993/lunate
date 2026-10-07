using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>Writes text files inside the workspace, creating parent directories as needed.</summary>
public sealed class WriteTool(
    Workspace workspace,
    IFileMutationQueue? mutations = null,
    IFileChangeSink? changes = null
) : ITool
{
    private readonly IFileMutationQueue _mutations = mutations ?? FileMutationQueue.Shared;

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

    public async Task<ToolResult> ExecuteAsync(
        JsonElement args,
        ToolContext ctx,
        CancellationToken ct
    )
    {
        if (args.ValueKind != JsonValueKind.Object)
        {
            return Error("arguments must be a JSON object");
        }

        if (
            !args.TryGetProperty("path", out var pathElement)
            || pathElement.ValueKind != JsonValueKind.String
        )
        {
            return Error("path is required and must be a string");
        }

        if (
            !args.TryGetProperty("content", out var contentElement)
            || contentElement.ValueKind != JsonValueKind.String
        )
        {
            return Error("content is required and must be a string");
        }

        var content = contentElement.GetString()!;
        if (!workspace.TryResolve(pathElement.GetString()!, out var resolved, out var error))
        {
            return Error(error);
        }

        if (Directory.Exists(resolved.AbsolutePath))
        {
            return Error($"{resolved.RelativePath} is a directory");
        }

        string target = resolved.AbsolutePath;
        string relative = resolved.RelativePath;
        return await _mutations.RunAsync(
            target,
            _ =>
            {
                var created = !File.Exists(target);
                var oldText = created ? string.Empty : TextFile.ReadAllText(target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, content, Utf8NoBom);
                changes?.Notify(target);

                var lines = CountLines(content);
                var details = new WriteDetails(
                    relative,
                    created,
                    lines,
                    LineDiff.Unified(oldText, content, relative)
                );
                var output = string.Create(
                    CultureInfo.InvariantCulture,
                    $"wrote {lines} lines to {relative} ({(created ? "created" : "replaced")})"
                );

                return Task.FromResult(new ToolResult(output, IsError: false, details));
            },
            ct
        );
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
