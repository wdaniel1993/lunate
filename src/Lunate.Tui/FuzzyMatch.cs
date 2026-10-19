namespace Lunate.Tui;

/// <summary>
/// The pure fuzzy matcher behind autocomplete (design D3): a case-insensitive subsequence scorer
/// where every query character must appear in the candidate in order, preferring a prefix match,
/// then a word-start match, then the earliest positions; ties break by shorter candidate, then
/// ordinal. <see cref="Filter"/> returns the ranked subset of items.
/// </summary>
internal static class FuzzyMatch
{
    /// <summary>The score of <paramref name="candidate"/> for <paramref name="query"/>; null = no match.</summary>
    public static int? Score(string query, string candidate)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(candidate);
        if (query.Length == 0)
        {
            return 0;
        }

        int position = 0;
        var score = 0;
        var first = true;
        foreach (char character in query)
        {
            int found = IndexOf(candidate, character, position);
            if (found < 0)
            {
                return null;
            }

            if (first)
            {
                score +=
                    found == 0 ? 300
                    : IsWordStart(candidate, found) ? 200
                    : 100 - Math.Min(99, found);
                first = false;
            }
            else
            {
                // Adjacent characters keep the score high; gaps cost, bounded so a long gap never
                // outweighs a prefix match.
                int gap = found - position;
                score += gap == 0 ? 20 : Math.Max(0, 10 - Math.Min(10, gap));
            }

            position = found + 1;
        }

        // A shorter candidate wins the tie; the ordinal comparison is applied by Filter.
        return score * 10_000 - candidate.Length;
    }

    /// <summary>
    /// The ranked items matching <paramref name="query"/>; an empty query returns the items in
    /// their natural order, no match returns an empty list. Ties break by shorter text, then
    /// ordinal.
    /// </summary>
    public static IReadOnlyList<CompletionItem> Filter(
        IReadOnlyList<CompletionItem> items,
        string query
    )
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0)
        {
            return items;
        }

        List<(CompletionItem Item, int Score)> matches = [];
        foreach (CompletionItem item in items)
        {
            if (Score(query, item.Text) is { } score)
            {
                matches.Add((item, score));
            }
        }

        matches.Sort(
            (left, right) =>
            {
                int byScore = right.Score.CompareTo(left.Score);
                if (byScore != 0)
                {
                    return byScore;
                }

                int byLength = left.Item.Text.Length.CompareTo(right.Item.Text.Length);
                return byLength != 0
                    ? byLength
                    : string.CompareOrdinal(left.Item.Text, right.Item.Text);
            }
        );
        return [.. matches.Select(match => match.Item)];
    }

    private static int IndexOf(string candidate, char character, int start)
    {
        for (var index = start; index < candidate.Length; index++)
        {
            if (char.ToUpperInvariant(candidate[index]) == char.ToUpperInvariant(character))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsWordStart(string candidate, int index) =>
        index > 0 && candidate[index - 1] is '/' or ':' or '-' or '_' or ' ' or '.';
}
