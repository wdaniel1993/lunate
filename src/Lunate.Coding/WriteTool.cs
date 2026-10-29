using System.Globalization;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>
/// Writes text files inside the workspace, creating parent directories as needed. An existing file
/// keeps its dominant line ending and its byte-order mark; a new file follows
/// <c>.editorconfig</c>, then <c>.gitattributes</c>, else the platform default.
/// </summary>
public sealed class WriteTool(
    Workspace workspace,
    IFileMutationQueue? mutations = null,
    ITextFileAccess? files = null
) : ITool
{
    /// <summary>The model-facing name; also what <see cref="HighRiskCall"/> classifies.</summary>
    internal const string ToolName = "write";

    private readonly IFileMutationQueue _mutations = mutations ?? FileMutationQueue.Shared;

    private readonly ITextFileAccess _files = files ?? LocalTextFileAccess.Instance;

    private readonly CredentialRedactor? _redaction;

    internal WriteTool(
        Workspace workspace,
        IFileMutationQueue? mutations,
        ITextFileAccess? files,
        CredentialRedactor? redaction
    )
        : this(workspace, mutations, files)
    {
        _redaction = redaction;
    }

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

    public string Name => ToolName;

    public string Description =>
        "Write a text file into the workspace; creates parent directories. An existing file keeps "
        + "its line endings and byte-order mark, and a new file follows the project's convention.";

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
                bool created;
                string oldText;
                string ending;
                bool hasBom;
                try
                {
                    created = !_files.Exists(target);
                    if (created)
                    {
                        oldText = string.Empty;
                        ending = LineEndingPolicy.ResolveEnding(target);
                        hasBom = LineEndingPolicy.ResolveBom(target);
                    }
                    else
                    {
                        (oldText, hasBom) = _files.ReadRaw(target);
                        ending = TextFile.DominantEnding(oldText);
                    }
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException)
                {
                    return Task.FromResult(Error($"could not be read: {exception.Message}"));
                }

                // Under the same mutation lock the write itself uses: refused means the file's
                // bytes are untouched.
                if (
                    RedactionRefusal.ForWrite(
                        content,
                        created ? null : oldText,
                        relative,
                        _redaction
                    ) is
                    { } refusal
                )
                {
                    return Task.FromResult(refusal);
                }

                // The file's own convention wins over the text as given: a CRLF file stays CRLF
                // and a byte-order mark survives, so a rewrite never turns into a whole-file diff.
                var written = content.ReplaceLineEndings(ending);
                try
                {
                    if (created)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    }

                    _files.WriteRaw(target, written, hasBom);
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException)
                {
                    return Task.FromResult(Error($"could not be written: {exception.Message}"));
                }

                ctx.FileChanges.Notify(
                    new FileChangeEvent(
                        target,
                        relative,
                        ctx.WorkingDirectory,
                        ctx.RunId,
                        created ? FileChangeKind.Created : FileChangeKind.Modified
                    )
                );

                var lines = CountLines(written);
                var details = new WriteDetails(
                    relative,
                    created,
                    lines,
                    LineDiff.Unified(oldText, written, relative)
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
