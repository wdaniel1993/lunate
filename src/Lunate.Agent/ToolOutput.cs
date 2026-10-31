using System.Globalization;
using System.Text;

namespace Lunate.Agent;

/// <summary>
/// Bounds tool output before the loop appends it to the history: output at or under the
/// limit passes through untouched; longer output keeps the first half and the last half of the
/// budget around a marker that states the omitted character count. Cut points never split a UTF-16
/// surrogate pair, and tiny limits degrade to the marker plus what fits instead of throwing.
/// </summary>
public static class ToolOutput
{
    private const int PointerPreviewLimit = 2_000;

    internal const int DefaultSpillThresholdTokens = 2_000;

    /// <summary>The default output budget, in characters.</summary>
    public const int DefaultLimit = 30_000;

    /// <summary>Estimates tokens using the harness's UTF-8 bytes / 4 rule.</summary>
    internal static long EstimateTokens(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return Encoding.UTF8.GetByteCount(output) / 4L;
    }

    /// <summary>Returns whether either the token threshold or character cap requires a spill.</summary>
    internal static bool ExceedsSpillLimits(string output, int tokenThreshold, int characterLimit)
    {
        ArgumentNullException.ThrowIfNull(output);
        return EstimateTokens(output) > tokenThreshold || output.Length > characterLimit;
    }

    /// <summary>
    /// Renders a bounded head/tail preview and an actionable pointer to the full output. When the
    /// answer already accompanies a separate transcript, pass <paramref name="includePreview"/>
    /// false so the model receives only the pointer line.
    /// </summary>
    internal static string RenderPointer(
        string output,
        string spillPath,
        bool includePreview = true
    )
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(spillPath);

        string preview = includePreview ? RenderPreview(output) : string.Empty;
        int omittedCharacters = output.Length - preview.Length;
        string marker = string.Create(
            CultureInfo.InvariantCulture,
            $"... [{omittedCharacters} characters omitted; {EstimateTokens(output)} tokens; {CountLines(output)} lines; full output at {spillPath}] ... read it with offset and limit."
        );
        return preview.Length == 0 ? marker : preview + "\n\n" + marker;
    }

    /// <summary>
    /// Returns <paramref name="output"/> unchanged when it fits the budget; otherwise returns head,
    /// marker and tail. The result is the content budget plus the marker.
    /// </summary>
    public static string Truncate(string output, int limit = DefaultLimit) =>
        TruncateCore(output, limit, spillPath: null, tooLarge: false);

    /// <summary>The same truncation with the absolute path of the written spill file in its marker.</summary>
    internal static string Truncate(string output, int limit, string spillPath) =>
        TruncateCore(output, limit, spillPath, tooLarge: false);

    /// <summary>The same truncation, saying the output was over the spill ceiling.</summary>
    internal static string Truncate(string output, int limit, bool tooLarge) =>
        TruncateCore(output, limit, spillPath: null, tooLarge);

    private static string TruncateCore(string output, int limit, string? spillPath, bool tooLarge)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (output.Length <= limit)
        {
            return output;
        }

        int contentBudget = Math.Max(limit, 0);
        int headLength = contentBudget / 2;
        int tailLength = contentBudget - headLength;

        if (headLength > 0 && char.IsHighSurrogate(output[headLength - 1]))
        {
            headLength--;
        }

        int tailStart = output.Length - tailLength;
        if (tailLength > 0 && char.IsLowSurrogate(output[tailStart]))
        {
            tailStart++;
        }

        string head = output[..headLength];
        string tail = output[tailStart..];
        int omitted = output.Length - head.Length - tail.Length;
        string count = omitted.ToString(CultureInfo.InvariantCulture);
        string marker = (spillPath, tooLarge) switch
        {
            ({ } path, _) => $"characters omitted; full output at {path}",
            (_, true) => "characters omitted; output too large to spill",
            _ => "characters truncated",
        };
        return head + "\n\n... [" + count + " " + marker + "] ...\n\n" + tail;
    }

    private static string RenderPreview(string output)
    {
        if (output.Length == 0)
        {
            return string.Empty;
        }

        string[] lines = output.Split('\n');
        int headCount = Math.Min(10, lines.Length);
        int tailStart = Math.Max(headCount, lines.Length - 10);
        string selected = string.Join('\n', lines.Take(headCount).Concat(lines.Skip(tailStart)));
        return selected.Length <= PointerPreviewLimit
            ? selected
            : CutHeadAndTail(selected, PointerPreviewLimit);
    }

    private static string CutHeadAndTail(string value, int limit)
    {
        int headLength = limit / 2;
        if (headLength > 0 && char.IsHighSurrogate(value[headLength - 1]))
        {
            headLength--;
        }

        int tailLength = limit - headLength;
        int tailStart = value.Length - tailLength;
        if (tailLength > 0 && char.IsLowSurrogate(value[tailStart]))
        {
            tailStart++;
        }

        return value[..headLength] + value[tailStart..];
    }

    private static int CountLines(string output)
    {
        if (output.Length == 0)
        {
            return 0;
        }

        int lines = 0;
        foreach (char character in output)
        {
            if (character == '\n')
            {
                lines++;
            }
        }

        return output[^1] == '\n' ? lines : lines + 1;
    }
}
