using System.Globalization;

namespace Lunate.Tui;

/// <summary>
/// Pure builder for a block header's meta: the tool-specific facts joined with <c>·</c> (ASCII
/// <c>-</c> without Unicode). Bash carries its exit code and scheduler-measured duration; edit and
/// write the unified diff's change counts.
/// </summary>
internal static class ToolBlockMeta
{
    public static string? Build(ToolBlockModel block, bool unicode = true)
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
            string minus = unicode ? "\u2212" : "-";
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"+{added} {minus}{removed}"));
        }
        else if (block.ToolName.StartsWith("cs_", StringComparison.Ordinal))
        {
            CsToolMeta.Append(block.Output, parts, unicode);
        }

        return parts.Count == 0 ? null : string.Join(unicode ? " \u00b7 " : " - ", parts);
    }
}
