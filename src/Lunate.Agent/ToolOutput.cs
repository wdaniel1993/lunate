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
}
