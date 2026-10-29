using System.Text;

namespace Lunate.Ai;

/// <summary>
/// Replaces the credential values Lunate itself holds with <c>[redacted:&lt;name&gt;]</c> at every
/// boundary where content leaves the live process: a session file, a spill file, an export.
/// The agent loop also applies the same redactor on the way into the context, so the model reads
/// the marker instead of the secret it would otherwise echo back.
/// Replacement is by exact value only: no pattern that "looks like a key" is touched, because a
/// guess produces both false negatives and false positives and teaches the reader to trust a filter
/// that cannot be trusted. What Lunate knows comes from the auth store and the provider
/// environment variables, the extension secret files and the MCP server environment values of the
/// session's config files; <c>Lunate.Coding</c> collects them and both the coding host and the agent
/// loop use this one implementation.
/// Replacement is one left-to-right scan: at each position a pre-existing
/// <c>[redacted:&lt;name&gt;]</c> marker is copied verbatim (so re-redacting text that already
/// carries markers changes nothing), otherwise the longest matching value is replaced by that
/// value's own marker without rescanning what was written, otherwise one character is copied.
/// A value that names another credential therefore cannot nest inside a written marker.
/// </summary>
internal sealed class CredentialRedactor
{
    /// <summary>
    /// Values shorter than this are skipped: replacing a two-character secret would rewrite the
    /// document without protecting anything a reader could use.
    /// </summary>
    internal const int MinimumValueLength = 4;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IReadOnlyList<(string Name, string Value)> _values;

    /// <summary>A redactor that knows nothing; the export still writes the review notice.</summary>
    internal static CredentialRedactor Empty { get; } = new([]);

    internal CredentialRedactor(IEnumerable<(string Name, string Value)> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values =
        [
            .. values
                .Where(entry =>
                    !string.IsNullOrWhiteSpace(entry.Value)
                    && entry.Value.Length >= MinimumValueLength
                )
                .Distinct()
                // Longest first: a shorter value that is a prefix of a longer one must not leave
                // the rest of the longer secret in place.
                .OrderByDescending(entry => entry.Value.Length),
        ];
    }

    /// <summary>How many distinct values the redactor will replace.</summary>
    internal int Count => _values.Count;

    /// <summary>Every value stored under one credential name (a name may carry several, e.g. a variable and its alias).</summary>
    internal IReadOnlyList<string> ValuesFor(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return [.. _values.Where(entry => entry.Name == name).Select(entry => entry.Value)];
    }

    /// <summary>
    /// The credential names a text carries as written, distinct and in order: the scanner owns the
    /// marker shape because <see cref="Marker"/> writes it.
    /// </summary>
    internal static IReadOnlyList<string> PlaceholderNamesIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        const string prefix = "[redacted:";
        List<string> names = [];
        int start = text.IndexOf(prefix, StringComparison.Ordinal);
        while (start >= 0)
        {
            int end = text.IndexOf(']', start + prefix.Length);
            if (end < 0)
            {
                break;
            }

            string name = text[(start + prefix.Length)..end];
            if (name.Length > 0 && !names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }

            start = text.IndexOf(prefix, end + 1, StringComparison.Ordinal);
        }

        return names;
    }

    /// <summary>The exact value/name pairs, longest value first (the tests read this).</summary>
    internal IReadOnlyList<(string Name, string Value)> Values => _values;

    /// <summary>
    /// The text with every known credential value replaced by its marker. A marker already present
    /// in <paramref name="text"/> is passed through untouched, so re-redacting redacted text is
    /// identity.
    /// </summary>
    internal string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length);
        int index = 0;
        while (index < text.Length)
        {
            // A pre-existing marker is copied verbatim and never re-matched: scanning resumes after
            // it, so a name that is also some other credential's value cannot nest inside it.
            if (text.AsSpan(index).StartsWith(MarkerPrefix, StringComparison.Ordinal))
            {
                int close = text.IndexOf(']', index + MarkerPrefix.Length);
                if (close > index + MarkerPrefix.Length)
                {
                    builder.Append(text, index, close + 1 - index);
                    index = close + 1;
                    continue;
                }
            }

            // Values are longest first, so the first match here is the longest one at this index.
            (string Name, string Value)? match = null;
            foreach ((string name, string value) in _values)
            {
                if (text.AsSpan(index).StartsWith(value, StringComparison.Ordinal))
                {
                    match = (name, value);
                    break;
                }
            }

            if (match is { } found)
            {
                builder.Append(Marker(found.Name));
                index += found.Value.Length;
                continue;
            }

            builder.Append(text[index]);
            index++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// The byte-level form, for the JSONL export: the session file is edited in place byte for
    /// byte, so a line without a credential stays exactly the bytes it was. The single marker-safe
    /// scan of <see cref="Redact"/> applies here too, so written markers are never nested.
    /// </summary>
    internal byte[] RedactUtf8(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        byte[] prefixBytes = Utf8NoBom.GetBytes(MarkerPrefix);
        (string Name, byte[] Value, byte[] Marker)[] replacements =
        [
            .. _values.Select(entry =>
                (
                    entry.Name,
                    Utf8NoBom.GetBytes(entry.Value),
                    Utf8NoBom.GetBytes(Marker(entry.Name))
                )
            ),
        ];
        if (
            source.AsSpan().IndexOf(prefixBytes) < 0
            && replacements.All(replacement => source.AsSpan().IndexOf(replacement.Value) < 0)
        )
        {
            return source;
        }

        const byte closeMarker = (byte)']';
        var builder = new List<byte>(source.Length);
        int index = 0;
        while (index < source.Length)
        {
            ReadOnlySpan<byte> remaining = source.AsSpan(index);
            if (remaining.StartsWith(prefixBytes))
            {
                int close = remaining.IndexOf(closeMarker);
                if (close > prefixBytes.Length)
                {
                    CopySpan(builder, remaining[..(close + 1)]);
                    index += close + 1;
                    continue;
                }
            }

            bool matched = false;
            foreach ((_, byte[] value, byte[] marker) in replacements)
            {
                if (remaining.StartsWith(value))
                {
                    CopySpan(builder, marker);
                    index += value.Length;
                    matched = true;
                    break;
                }
            }

            if (matched)
            {
                continue;
            }

            builder.Add(source[index]);
            index++;
        }

        return [.. builder];
    }

    /// <summary>The marker the session's export writes in place of a known value.</summary>
    internal static string Marker(string name) => "[redacted:" + name + "]";

    private const string MarkerPrefix = "[redacted:";

    private static void CopySpan(List<byte> builder, ReadOnlySpan<byte> span)
    {
        foreach (byte b in span)
        {
            builder.Add(b);
        }
    }
}
