using System.Globalization;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>Replaces one unique occurrence of <c>old_text</c> with <c>new_text</c> in a workspace text file.</summary>
public sealed class EditTool(
    Workspace workspace,
    IFileMutationQueue? mutations = null,
    IFileChangeSink? changes = null
) : ITool
{
    private readonly IFileMutationQueue _mutations = mutations ?? FileMutationQueue.Shared;

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
                  "description": "The exact text to replace; it must match exactly one place in the file"
                },
                "new_text": {
                  "type": "string",
                  "description": "The replacement text; the file's endings, BOM and trailing newline are kept"
                }
              },
              "required": ["path", "old_text", "new_text"]
            }
            """
        )
        .RootElement.Clone();

    public string Name => "edit";

    public string Description =>
        "Replace old_text with new_text in a workspace text file; old_text must match exactly one place "
        + "(line-ending and trailing-whitespace differences are tolerated).";

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

        if (!File.Exists(resolved.AbsolutePath))
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
                    (fileText, hasBom) = TextFile.ReadRaw(resolved.AbsolutePath);
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

                var normalized = false;
                var matches = FindMatches(fileLines, oldLines, normalized: false);
                if (matches.Count == 0)
                {
                    normalized = true;
                    matches = FindMatches(fileLines, oldLines, normalized: true);
                }

                if (matches.Count == 0)
                {
                    return Task.FromResult(
                        Error($"could not find old_text in {resolved.RelativePath}")
                    );
                }

                if (matches.Count > 1)
                {
                    return Task.FromResult(Error(Ambiguity(matches, resolved.RelativePath)));
                }

                var start = matches[0];
                var tier = normalized ? "normalized" : "exact";
                var resultLines = ReplaceLines(
                    StripTrailingCarriageReturns(fileLines),
                    start,
                    oldLines.Length,
                    newLines
                );
                var ending = TextFile.DominantEnding(fileText);
                var newFileText = string.Join(ending, resultLines);
                if (fileEndsWithNewline)
                {
                    newFileText += ending;
                }

                TextFile.WriteRaw(resolved.AbsolutePath, newFileText, hasBom);
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

    private static List<int> FindMatches(string[] lines, string[] pattern, bool normalized)
    {
        var matches = new List<int>();
        if (pattern.Length > lines.Length)
        {
            return matches;
        }

        for (var start = 0; start <= lines.Length - pattern.Length; start++)
        {
            var matched = true;
            for (var offset = 0; offset < pattern.Length; offset++)
            {
                if (!LineEquals(lines[start + offset], pattern[offset], normalized))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                matches.Add(start);
            }
        }

        return matches;
    }

    private static bool LineEquals(string line, string patternLine, bool normalized) =>
        normalized ? line.TrimEnd() == patternLine.TrimEnd() : line == patternLine;

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

    private static string Ambiguity(List<int> matches, string relativePath)
    {
        var lines = string.Join(
            ", ",
            matches.Select(match => (match + 1).ToString(CultureInfo.InvariantCulture))
        );

        return string.Create(
            CultureInfo.InvariantCulture,
            $"old_text matches {matches.Count} places in {relativePath} at lines {lines}; make it longer so it matches once"
        );
    }

    private static ToolResult Error(string output) => new(output, IsError: true);
}
