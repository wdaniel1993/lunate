using System.Globalization;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>Replaces one unique occurrence of <c>old_text</c> with <c>new_text</c> in a workspace text file.</summary>
public sealed class EditTool(
    Workspace workspace,
    IFileMutationQueue? mutations = null,
    IFileChangeSink? changes = null,
    ITextFileAccess? files = null
) : ITool
{
    private readonly IFileMutationQueue _mutations = mutations ?? FileMutationQueue.Shared;

    private readonly ITextFileAccess _files = files ?? LocalTextFileAccess.Instance;

    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Path of the file to edit, relative to the workspace or absolute inside it"
                },
                "old_text": {
                  "type": "string",
                  "description": "The text to replace; it must match one place in the file (use start_line when it matches several)"
                },
                "new_text": {
                  "type": "string",
                  "description": "The replacement text; the file's endings, BOM and trailing newline are kept"
                },
                "start_line": {
                  "type": "integer",
                  "minimum": 1,
                  "description": "Optional 1-based line where old_text starts; disambiguates when old_text matches several places"
                }
              },
              "required": ["path", "old_text", "new_text"]
            }
            """
        )
        .RootElement.Clone();

    public string Name => "edit";

    public string Description =>
        "Replace old_text with new_text in a workspace text file; matching tolerates line-ending, "
        + "trailing-whitespace and indentation differences (start_line disambiguates several matches).";

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
            !args.TryGetProperty("old_text", out var oldElement)
            || oldElement.ValueKind != JsonValueKind.String
        )
        {
            return Error("old_text is required and must be a string");
        }

        if (
            !args.TryGetProperty("new_text", out var newElement)
            || newElement.ValueKind != JsonValueKind.String
        )
        {
            return Error("new_text is required and must be a string");
        }

        int? startLine = null;
        if (args.TryGetProperty("start_line", out var startElement))
        {
            if (
                startElement.ValueKind != JsonValueKind.Number
                || !startElement.TryGetInt32(out var requestedStart)
                || requestedStart < 1
            )
            {
                return Error("start_line must be an integer greater than or equal to 1");
            }

            startLine = requestedStart;
        }

        var oldText = oldElement.GetString()!;
        var newText = newElement.GetString()!;

        if (!workspace.TryResolve(pathElement.GetString()!, out var resolved, out var error))
        {
            return Error(error);
        }

        if (Directory.Exists(resolved.AbsolutePath))
        {
            return Error($"{resolved.RelativePath} is a directory");
        }

        bool exists;
        try
        {
            exists = _files.Exists(resolved.AbsolutePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Error($"could not be read: {exception.Message}");
        }

        if (!exists)
        {
            return Error($"file not found: {resolved.RelativePath}");
        }

        if (oldText.Length == 0)
        {
            return Error("old_text must not be empty");
        }

        if (oldText == newText)
        {
            return Error("old_text and new_text are identical; nothing to change");
        }

        return await _mutations.RunAsync(
            resolved.AbsolutePath,
            _ =>
            {
                string fileText;
                bool hasBom;
                try
                {
                    (fileText, hasBom) = _files.ReadRaw(resolved.AbsolutePath);
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException)
                {
                    return Task.FromResult(Error($"could not be read: {exception.Message}"));
                }

                var (fileLines, fileEndsWithNewline) = SplitRaw(fileText);
                var (oldLines, _) = SplitRaw(oldText);
                var (newRawLines, _) = SplitRaw(newText);
                var newLines = StripTrailingCarriageReturns(newRawLines);

                var matches = EditMatcher.FindMatches(fileLines, oldLines, normalized: false);
                var tier = "exact";
                List<IndentMatch>? indentMatches = null;
                if (matches.Count == 0)
                {
                    matches = EditMatcher.FindMatches(fileLines, oldLines, normalized: true);
                    tier = "normalized";
                }

                if (
                    matches.Count == 0
                    && !EditMatcher.IsWhitespaceSignificant(resolved.RelativePath)
                )
                {
                    indentMatches = EditMatcher.FindIndentMatches(fileLines, oldLines);
                    matches = [.. indentMatches.Select(match => match.Start)];
                    tier = "indent";
                }

                if (matches.Count == 0)
                {
                    return Task.FromResult(
                        Error(EditMatcher.NotFound(fileLines, oldLines, resolved.RelativePath))
                    );
                }

                var selected = matches;
                if (matches.Count > 1 && startLine is int requestedStart)
                {
                    var within = matches
                        .Where(match => Math.Abs((long)match + 1 - requestedStart) <= 3)
                        .ToList();
                    if (within.Count == 1)
                    {
                        selected = within;
                    }
                }

                if (selected.Count > 1)
                {
                    return Task.FromResult(
                        Error(EditMatcher.Ambiguity(matches, resolved.RelativePath))
                    );
                }

                var start = selected[0];
                var replacement = newLines;
                if (indentMatches is not null)
                {
                    var offset = indentMatches.First(match => match.Start == start).Offset;
                    replacement = EditMatcher.IndentReplacement(newLines, offset);
                }

                var resultLines = ReplaceLines(
                    StripTrailingCarriageReturns(fileLines),
                    start,
                    oldLines.Length,
                    replacement
                );
                var ending = TextFile.DominantEnding(fileText);
                var newFileText = string.Join(ending, resultLines);
                if (fileEndsWithNewline)
                {
                    newFileText += ending;
                }

                try
                {
                    _files.WriteRaw(resolved.AbsolutePath, newFileText, hasBom);
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException)
                {
                    return Task.FromResult(Error($"could not be written: {exception.Message}"));
                }

                changes?.Notify(resolved.AbsolutePath);

                var firstLine = start + 1;
                var lastLine = start + newLines.Length;
                var output = string.Create(
                    CultureInfo.InvariantCulture,
                    $"edited {resolved.RelativePath} lines {firstLine}\u2013{lastLine} (match: {tier})"
                );
                var details = new EditDetails(
                    resolved.RelativePath,
                    firstLine,
                    lastLine,
                    tier,
                    LineDiff.Unified(fileText, newFileText, resolved.RelativePath)
                );

                return Task.FromResult(new ToolResult(output, IsError: false, details));
            },
            ct
        );
    }

    private static (string[] Lines, bool EndsWithNewline) SplitRaw(string text)
    {
        var raw = text.Split('\n');
        var endsWithNewline = raw[^1].Length == 0;
        var count = endsWithNewline ? raw.Length - 1 : raw.Length;
        var lines = new string[count];
        Array.Copy(raw, lines, count);

        return (lines, endsWithNewline);
    }

    private static string[] StripTrailingCarriageReturns(string[] lines)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].EndsWith('\r'))
            {
                lines[index] = lines[index][..^1];
            }
        }

        return lines;
    }

    private static string[] ReplaceLines(
        string[] lines,
        int start,
        int removeCount,
        string[] replacement
    )
    {
        var result = new string[lines.Length - removeCount + replacement.Length];
        Array.Copy(lines, 0, result, 0, start);
        Array.Copy(replacement, 0, result, start, replacement.Length);
        Array.Copy(
            lines,
            start + removeCount,
            result,
            start + replacement.Length,
            lines.Length - start - removeCount
        );

        return result;
    }

    private static ToolResult Error(string output) => new(output, IsError: true);
}
