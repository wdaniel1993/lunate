using System.Globalization;

namespace Lunate.Tui;

/// <summary>The exact text form of an attached image: <c>[image &lt;width&gt;x&lt;height&gt;]</c>.</summary>
internal static class ImagePlaceholder
{
    private const string Prefix = "[image ";
    private const string Suffix = "]";

    /// <summary>Whether the whole text is one placeholder with two positive invariant integers.</summary>
    public static bool IsOnly(string? text)
    {
        if (
            text is null
            || !text.StartsWith(Prefix, StringComparison.Ordinal)
            || !text.EndsWith(Suffix, StringComparison.Ordinal)
        )
        {
            return false;
        }

        string dimensions = text[Prefix.Length..^Suffix.Length];
        int separator = dimensions.IndexOf('x', StringComparison.Ordinal);
        if (separator <= 0 || separator != dimensions.LastIndexOf('x'))
        {
            return false;
        }

        return TryPositive(dimensions[..separator]) && TryPositive(dimensions[(separator + 1)..]);
    }

    private static bool TryPositive(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
        && number > 0
        && value[0] != '0';
}
