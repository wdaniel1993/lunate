namespace Lunate.Extensibility;

/// <summary>
/// Matches the documented file change pattern grammar: <c>*</c> matches any characters, <c>?</c>
/// exactly one, the pattern is anchored on the whole path, and comparison follows the platform
/// (case-sensitive on Linux, case-insensitive elsewhere). A null or empty pattern matches any path.
/// </summary>
internal static class PatternFilter
{
    public static bool Matches(string? pattern, string path)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return true;
        }

        bool ignoreCase = !OperatingSystem.IsLinux();
        int patternIndex = 0;
        int pathIndex = 0;
        int starIndex = -1;
        int retryPathIndex = 0;

        while (pathIndex < path.Length)
        {
            if (
                patternIndex < pattern.Length
                && (
                    pattern[patternIndex] == '?'
                    || Same(path[pathIndex], pattern[patternIndex], ignoreCase)
                )
            )
            {
                patternIndex++;
                pathIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex;
                retryPathIndex = pathIndex;
                patternIndex++;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                retryPathIndex++;
                pathIndex = retryPathIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    private static bool Same(char left, char right, bool ignoreCase) =>
        ignoreCase ? char.ToUpperInvariant(left) == char.ToUpperInvariant(right) : left == right;
}
