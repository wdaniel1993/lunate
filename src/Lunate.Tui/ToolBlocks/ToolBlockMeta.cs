using System.Globalization;

namespace Lunate.Tui;

/// <summary>
/// Pure builder for a block header's meta: the tool-specific facts joined with <c>·</c>. Bash
/// carries its exit code and scheduler-measured duration; edit and write the unified diff's
/// change counts.
/// </summary>
internal static class ToolBlockMeta
{
    public static string? Build(ToolBlockModel block)
    {
        ArgumentNullException.ThrowIfNull(block);

        var parts = new List<string>(3);
        if (block.ToolName == "bash")
        {
            if (block.ExitCode is { } exit)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"exit {exit}"));
            }

            if (block.Duration is { } duration)
            {
                parts.Add(ToolDuration.Format(duration));
            }
        }
        else if (
            (block.ToolName is "edit" or "write")
            && block.Diff is { } diff
            && !string.IsNullOrWhiteSpace(diff.UnifiedDiff)
        )
        {
            var (added, removed) = DiffRenderer.ChangeCounts(diff);
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"+{added} \u2212{removed}"));
        }

        return parts.Count == 0 ? null : string.Join(" \u00b7 ", parts);
    }
}
