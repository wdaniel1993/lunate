using System.Globalization;
using System.Text;
using Lunate.Agent;

namespace Lunate.Coding;

public sealed partial class ReadTool
{
    private static ToolResult ReadSpillPage(
        string text,
        int offset,
        int spillThresholdTokens,
        int outputCharacterLimit
    )
    {
        long tokenByteBudget = ((long)spillThresholdTokens + 1) * 4 - 1;
        string remaining = text[offset..];
        if (
            remaining.Length <= outputCharacterLimit
            && Encoding.UTF8.GetByteCount(remaining) <= tokenByteBudget
        )
        {
            return Result(remaining);
        }

        string maxFooter = Continuation(text.Length);
        int contentCharacterBudget = outputCharacterLimit - maxFooter.Length;
        long contentByteBudget = tokenByteBudget - Encoding.UTF8.GetByteCount(maxFooter);
        if (contentCharacterBudget < 1 || contentByteBudget < 1)
        {
            return Error("output.spillThresholdTokens is too small to include a read continuation");
        }

        int end = AdvanceWithinBudget(text, offset, contentCharacterBudget, contentByteBudget);
        if (end == offset)
        {
            return Error("output.spillThresholdTokens is too small to read spill content");
        }

        string output = text[offset..end] + Continuation(end);
        return
            ToolOutput.EstimateTokens(output) <= spillThresholdTokens
            && output.Length <= outputCharacterLimit
            ? Result(output)
            : Error("could not fit a spill page within the configured output bounds");
    }

    private static int AdvanceWithinBudget(
        string text,
        int offset,
        int characterBudget,
        long byteBudget
    )
    {
        int end = offset;
        int characters = 0;
        long bytes = 0;
        foreach (Rune rune in text.AsSpan(offset).EnumerateRunes())
        {
            if (
                characters + rune.Utf16SequenceLength > characterBudget
                || bytes + rune.Utf8SequenceLength > byteBudget
            )
            {
                break;
            }

            characters += rune.Utf16SequenceLength;
            bytes += rune.Utf8SequenceLength;
            end += rune.Utf16SequenceLength;
        }

        return end;
    }

    private static string Continuation(int offset) =>
        string.Create(CultureInfo.InvariantCulture, $"\n[read continuation: offset {offset}]");
}
