namespace Lunate.Ai;

/// <summary>Match discovery and overlap resolution for <see cref="CredentialRedactor"/>.</summary>
internal sealed partial class CredentialRedactor
{
    /// <summary>
    /// Pass A: the <c>[redacted:&lt;name&gt;]</c>-shaped spans that qualify for verbatim
    /// pass-through, so marker-shaped text cannot smuggle a secret.
    /// </summary>
    private List<(int Start, int End)> MarkerSpans(string text)
    {
        List<(int Start, int End)> spans = [];
        int index = 0;
        while (index < text.Length)
        {
            if (text.AsSpan(index).StartsWith(MarkerPrefix, StringComparison.Ordinal))
            {
                int nameStart = index + MarkerPrefix.Length;
                int close = text.IndexOf(']', nameStart);
                if (
                    close > nameStart
                    && _names.Contains(text[nameStart..close], StringComparer.Ordinal)
                    && !ContainsValue(text.AsSpan(index, close + 1 - index))
                )
                {
                    spans.Add((index, close + 1));
                    index = close + 1;
                    continue;
                }
            }

            index++;
        }

        return spans;
    }

    private List<(int Start, int End)> MarkerSpans(ReadOnlySpan<byte> source, byte[] prefix)
    {
        const byte closeMarker = (byte)']';
        List<(int Start, int End)> spans = [];
        int index = 0;
        while (index < source.Length)
        {
            ReadOnlySpan<byte> remaining = source[index..];
            int close = remaining.IndexOf(closeMarker);
            if (
                remaining.StartsWith(prefix)
                && close > prefix.Length
                && IsKnownName(remaining[prefix.Length..close])
                && !ContainsValue(remaining[..(close + 1)])
            )
            {
                spans.Add((index, index + close + 1));
                index += close + 1;
                continue;
            }

            index++;
        }

        return spans;
    }

    /// <summary>Pass B: every occurrence of every known value, in scan order per entry.</summary>
    private List<(int Start, int Length, int Entry)> ValueMatches(string text)
    {
        List<(int Start, int Length, int Entry)> matches = [];
        for (int entry = 0; entry < _values.Count; entry++)
        {
            int start = text.IndexOf(_values[entry].Value, StringComparison.Ordinal);
            while (start >= 0)
            {
                matches.Add((start, _values[entry].Value.Length, entry));
                start = text.IndexOf(_values[entry].Value, start + 1, StringComparison.Ordinal);
            }
        }

        return matches;
    }

    private List<(int Start, int Length, int Entry)> ValueMatches(ReadOnlySpan<byte> source)
    {
        List<(int Start, int Length, int Entry)> matches = [];
        for (int entry = 0; entry < _valueBytes.Length; entry++)
        {
            int start = 0;
            int found;
            while ((found = source[start..].IndexOf(_valueBytes[entry])) >= 0)
            {
                matches.Add((start + found, _valueBytes[entry].Length, entry));
                start += found + 1;
            }
        }

        return matches;
    }

    /// <summary>
    /// Passes C and D: accept candidates longest first — ties by earlier start, then entry order,
    /// keeping the old stable-insertion outcome for equal values — dropping any whose span
    /// intersects an accepted span or a protected marker span (the same precedence the
    /// left-to-right scan gave them); the plan merges the winners with the protected spans.
    /// </summary>
    private static List<(int Start, int End, int Entry)> EmitPlan(
        List<(int Start, int Length, int Entry)> matches,
        List<(int Start, int End)> protectedSpans
    )
    {
        List<(int Start, int Length, int Entry)> accepted = [];
        foreach (
            var (start, length, entry) in matches
                .OrderByDescending(match => match.Length)
                .ThenBy(match => match.Start)
                .ThenBy(match => match.Entry)
        )
        {
            bool overlaps =
                protectedSpans.Any(s => start < s.End && s.Start < start + length)
                || accepted.Any(a => start < a.Start + a.Length && a.Start < start + length);
            if (!overlaps)
            {
                accepted.Add((start, length, entry));
            }
        }

        List<(int Start, int End, int Entry)> plan =
        [
            .. protectedSpans.Select(span => (span.Start, span.End, Protected)),
            .. accepted.Select(match => (match.Start, match.Start + match.Length, match.Entry)),
        ];
        plan.Sort((a, b) => a.Start - b.Start);
        return plan;
    }

    private bool ContainsValue(ReadOnlySpan<char> text)
    {
        foreach ((string _, string value) in _values)
        {
            if (text.Contains(value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool ContainsValue(ReadOnlySpan<byte> span)
    {
        foreach (byte[] value in _valueBytes)
        {
            if (span.IndexOf(value) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsKnownName(ReadOnlySpan<byte> name)
    {
        foreach (byte[] known in _nameBytes)
        {
            if (name.SequenceEqual(known))
            {
                return true;
            }
        }

        return false;
    }
}
