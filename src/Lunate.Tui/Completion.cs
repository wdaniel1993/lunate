namespace Lunate.Tui;

/// <summary>
/// The outcome of a completion attempt: either a replacement for the typed word (non-null
/// <see cref="Replacement"/>) or the candidates to show when no progress is possible.
/// </summary>
public sealed record CompletionResult(string? Replacement, IReadOnlyList<string> Candidates)
{
    /// <summary>Whether the result replaces the input instead of listing candidates.</summary>
    public bool IsReplacement => Replacement is not null;
}

/// <summary>
/// The pure slash-command completion helper: one match completes to the full command, several
/// matches complete to their longest common prefix, and when the prefix makes no progress the
/// sorted candidates are returned for a notice. The candidate list is supplied by the caller so
/// the helper stays free of the built-in command table (and grows a file index for <c>@path</c>
/// completion later).
/// </summary>
public static class Completion
{
    /// <summary>Completes <paramref name="text"/> against <paramref name="candidates"/>.</summary>
    public static CompletionResult Complete(string text, IReadOnlyList<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(candidates);

        List<string> matches = [];
        foreach (string candidate in candidates)
        {
            if (candidate.StartsWith(text, StringComparison.Ordinal))
            {
                matches.Add(candidate);
            }
        }

        if (matches.Count == 0)
        {
            return new CompletionResult(null, []);
        }

        if (matches.Count == 1)
        {
            return new CompletionResult(matches[0], []);
        }

        string prefix = LongestCommonPrefix(matches);
        if (prefix.Length > text.Length)
        {
            return new CompletionResult(prefix, []);
        }

        matches.Sort(StringComparer.Ordinal);
        return new CompletionResult(null, matches);
    }

    private static string LongestCommonPrefix(IReadOnlyList<string> values)
    {
        string prefix = values[0];
        for (var index = 1; index < values.Count && prefix.Length > 0; index++)
        {
            string value = values[index];
            int length = Math.Min(prefix.Length, value.Length);
            var shared = 0;
            while (shared < length && prefix[shared] == value[shared])
            {
                shared++;
            }

            prefix = prefix[..shared];
        }

        return prefix;
    }
}
