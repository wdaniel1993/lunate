using System.Globalization;

namespace Lunate.Agent;

/// <summary>
/// Bounds tool output before the loop appends it to the history: output at or under the
/// limit passes through untouched; longer output keeps the first half and the last half of the
/// budget around a marker that states the omitted character count. Cut points never split a UTF-16
/// surrogate pair, and tiny limits degrade to the marker plus what fits instead of throwing.
/// </summary>
public static class ToolOutput
{
    /// <summary>The default output budget, in characters.</summary>
    public const int DefaultLimit = 30_000;

    /// <summary>
    /// Returns <paramref name="output"/> unchanged when it fits the budget; otherwise returns head,
    /// marker and tail. The result is the content budget plus the marker.
    /// </summary>
    public static string Truncate(string output, int limit = DefaultLimit)
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
        return head
            + "\n\n... ["
            + omitted.ToString(CultureInfo.InvariantCulture)
            + " characters truncated] ...\n\n"
            + tail;
    }
}
