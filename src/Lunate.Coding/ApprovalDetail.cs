using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Tui;

namespace Lunate.Coding;

/// <summary>
/// Builds the prepared approval-panel lines for a tool call: the first line is the headline
/// action (command, <c>path  +N −M</c>, read range or summary) and the rest is the detail block
/// (diff lines, extra command lines). Unknown shapes fall back to the raw arguments. Pure apart
/// from the existing-file read behind a <c>write</c> preview; the output is bounded to
/// <see cref="ApprovalPromptModel.MaxDetailLines"/> block lines plus a marker.
/// </summary>
internal static class ApprovalDetail
{
    private const string Elision = "\u2026";
    private const string AsciiElision = "...";
    private const string Minus = "\u2212";

    public static IReadOnlyList<string> Build(
        string toolName,
        JsonElement args,
        string? workspaceRoot,
        bool unicode,
        int width
    )
    {
        int contentWidth = Math.Max(1, width - 2);
        if (args.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return [];
        }

        if (args.ValueKind == JsonValueKind.Object)
        {
            if (toolName == "bash" && Text(args, "command") is { } command)
            {
                return Wrapped(command, unicode, contentWidth);
            }

            if (
                toolName == "edit"
                && Text(args, "path") is { } editPath
                && Text(args, "old_text") is { } oldText
                && Text(args, "new_text") is { } newText
            )
            {
                return Diff(editPath, oldText, newText, unicode);
            }

            if (
                toolName == "write"
                && Text(args, "path") is { } writePath
                && Text(args, "content") is { } content
            )
            {
                return Diff(writePath, ExistingText(writePath, workspaceRoot), content, unicode);
            }
        }

        string raw = args.GetRawText().Trim();
        string summary = ToolArgsSummary.Summarize(toolName, args.GetRawText(), unicode: unicode);
        if (toolName == "read")
        {
            return summary.Length > 0 ? [summary] : [];
        }

        if (summary.Length > 0 && summary != raw)
        {
            return [summary];
        }

        return Wrapped(raw, unicode, contentWidth);
    }

    private static string? Text(JsonElement args, string name) =>
        args.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    /// <summary>A command or raw-argument block: hard lines wrapped, headline first, bounded.</summary>
    private static IReadOnlyList<string> Wrapped(string text, bool unicode, int width)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return [];
        }

        var lines = new List<string>();
        foreach (string raw in trimmed.Split('\n'))
        {
            string line = raw.EndsWith('\r') ? raw[..^1] : raw;
            lines.AddRange(Wrap(line, width));
        }

        return Bound(lines, unicode);
    }

    private static IReadOnlyList<string> Diff(
        string path,
        string oldText,
        string newText,
        bool unicode
    )
    {
        var info = new ToolDiffInfo(path, "", LineDiff.Unified(oldText, newText, path));
        var (added, removed) = DiffRenderer.ChangeCounts(info);
        string shown = PathText.Shorten(path, PathText.HeaderBudget, PathText.Home(), unicode);
        string minus = unicode ? Minus : "-";
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"{shown}  +{added} {minus}{removed}"),
        };
        foreach (var line in DiffRenderer.Parse(info))
        {
            if (line.Kind != DiffLineKind.FileHeader)
            {
                lines.Add(line.Text);
            }
        }

        return Bound(lines, unicode);
    }

    /// <summary>The existing file's text when it resolves inside the root, else empty.</summary>
    private static string ExistingText(string path, string? workspaceRoot)
    {
        if (string.IsNullOrEmpty(workspaceRoot))
        {
            return string.Empty;
        }

        try
        {
            var workspace = new Workspace(workspaceRoot);
            return workspace.TryResolve(path, out var resolved, out _)
                ? TextFile.ReadAllText(resolved.AbsolutePath)
                : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>Keeps the headline and at most the block budget, then names the dropped line count.</summary>
    private static IReadOnlyList<string> Bound(IReadOnlyList<string> lines, bool unicode)
    {
        if (lines.Count == 0)
        {
            return [];
        }

        // The headline carries the first line; the block budget covers the lines below it.
        int budget = ApprovalPromptModel.MaxDetailLines - 1;
        int body = lines.Count - 1;
        if (body <= budget)
        {
            return lines;
        }

        var bounded = new List<string>(budget + 2);
        for (int index = 0; index <= budget; index++)
        {
            bounded.Add(lines[index]);
        }

        int hidden = body - budget;
        string marker = unicode ? Elision : AsciiElision;
        bounded.Add(string.Create(CultureInfo.InvariantCulture, $"{marker} +{hidden} lines"));
        return bounded;
    }

    /// <summary>One hard line cut at the cell budget, words kept whole when a space allows.</summary>
    private static IEnumerable<string> Wrap(string text, int width)
    {
        string remaining = text.TrimEnd();
        while (remaining.Length > 0)
        {
            if (CellText.Width(remaining) <= width)
            {
                yield return remaining;
                yield break;
            }

            int index = BreakIndex(remaining, width);
            yield return remaining[..index].TrimEnd();
            remaining = remaining[index..].TrimStart(' ');
        }
    }

    private static int BreakIndex(string text, int width)
    {
        int used = 0;
        int index = 0;
        int space = -1;
        while (index < text.Length)
        {
            if (
                Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out int consumed)
                != OperationStatus.Done
            )
            {
                break;
            }

            int runeWidth = CellText.Width(rune);
            if (used + runeWidth > width)
            {
                break;
            }

            if (rune.Value == ' ')
            {
                space = index;
            }

            used += runeWidth;
            index += consumed;
        }

        return space > 0 ? space
            : index > 0 ? index
            : text.Length;
    }
}
