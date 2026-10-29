using System.Globalization;

namespace Lunate.Coding;

/// <summary>
/// The small semantic version the update check compares: three numeric parts, an optional
/// <c>-prerelease</c> and an ignored <c>+build</c>. Prerelease precedence follows semver 2.0: a
/// prerelease sorts before its release, numeric identifiers compare numerically and sort before
/// alphanumeric ones, and a shorter identifier list sorts before a longer one with the same prefix.
/// </summary>
internal readonly record struct SemanticVersion(int Major, int Minor, int Patch, string? Prerelease)
    : IComparable<SemanticVersion>
{
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string remaining = text.Trim();
        if (remaining.StartsWith('v') || remaining.StartsWith('V'))
        {
            remaining = remaining[1..];
        }

        int plus = remaining.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            if (plus == remaining.Length - 1)
            {
                return false;
            }

            remaining = remaining[..plus];
        }

        string? prerelease = null;
        int dash = remaining.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            prerelease = remaining[(dash + 1)..];
            remaining = remaining[..dash];
            if (prerelease.Length == 0 || !IsPrerelease(prerelease))
            {
                return false;
            }
        }

        string[] parts = remaining.Split('.');
        if (
            parts.Length != 3
            || !TryNumber(parts[0], out int major)
            || !TryNumber(parts[1], out int minor)
            || !TryNumber(parts[2], out int patch)
        )
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        int core = Major.CompareTo(other.Major);
        if (core != 0)
        {
            return core;
        }

        core = Minor.CompareTo(other.Minor);
        if (core != 0)
        {
            return core;
        }

        core = Patch.CompareTo(other.Patch);
        if (core != 0)
        {
            return core;
        }

        if (Prerelease is null)
        {
            return other.Prerelease is null ? 0 : 1;
        }

        return other.Prerelease is null ? -1 : ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static bool TryNumber(string text, out int number)
    {
        number = 0;
        return text.Length > 0
            && text.All(char.IsAsciiDigit)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static bool IsPrerelease(string text) =>
        text.Split('.')
            .All(identifier =>
                identifier.Length > 0
                && identifier.All(character =>
                    char.IsAsciiLetterOrDigit(character) || character == '-'
                )
            );

    private static int ComparePrerelease(string left, string right)
    {
        string[] leftParts = left.Split('.');
        string[] rightParts = right.Split('.');
        int shared = Math.Min(leftParts.Length, rightParts.Length);
        for (var index = 0; index < shared; index++)
        {
            string first = leftParts[index];
            string second = rightParts[index];
            bool firstNumeric = first.All(char.IsAsciiDigit);
            bool secondNumeric = second.All(char.IsAsciiDigit);
            int comparison =
                firstNumeric && secondNumeric ? CompareNumeric(first, second)
                : firstNumeric ? -1
                : secondNumeric ? 1
                : string.CompareOrdinal(first, second);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }

    /// <summary>
    /// Numeric identifiers compare by value: leading zeros are not significant and a larger digit
    /// count wins, so no parse can overflow on a hostile release tag.
    /// </summary>
    private static int CompareNumeric(string left, string right)
    {
        string first = left.TrimStart('0');
        string second = right.TrimStart('0');
        if (first.Length != second.Length)
        {
            return first.Length.CompareTo(second.Length);
        }

        return string.CompareOrdinal(first, second);
    }
}
