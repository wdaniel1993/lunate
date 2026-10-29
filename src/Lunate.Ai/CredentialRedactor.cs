using System.Security.Cryptography;
using System.Text;

namespace Lunate.Ai;

/// <summary>
/// Replaces the credential values Lunate itself holds with <c>[redacted:&lt;name&gt;]</c> at every
/// boundary where content leaves the live process: a session file, a spill file, an export; the
/// agent loop also applies the same redactor on the way into the context, so the model reads the
/// marker instead of the secret it would otherwise echo back.
/// Replacement is by exact value only: no pattern that "looks like a key" is touched, because a
/// guess produces both false negatives and false positives. What Lunate knows comes from the auth
/// store, the provider environment variables, the extension secret files and the MCP server
/// environment values of the session's config files; <c>Lunate.Coding</c> collects them and both
/// the coding host and the agent loop use this one implementation.
/// Invariant: after redaction no known credential value remains anywhere in the output — not as
/// itself, not as a substring — including inside a marker label and inside a passed-through marker
/// span. Each entry therefore emits one precomputed marker labelled with the entry's name; when the
/// name contains a known value as a substring, the label is encoded instead: the first 12 lowercase
/// hex characters of SHA-256 over the name's UTF-8 bytes. Values shorter than
/// <see cref="MinimumValueLength"/> are never known values.
/// Replacement is one left-to-right scan: at each position a pre-existing
/// <c>[redacted:&lt;name&gt;]</c> marker is copied verbatim only when the bracketed name is exactly
/// one of the credential names this redactor knows and the copied span contains no known value —
/// any other bracketed span falls through to value matching — otherwise the longest matching value
/// is replaced by its own precomputed marker without rescanning what was written, otherwise one
/// character is copied. A value that names another credential therefore cannot nest inside a
/// written marker.
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
    private readonly string[] _names;
    private readonly byte[][] _nameBytes;
    private readonly string[] _markers;
    private readonly byte[][] _valueBytes;
    private readonly byte[][] _markerBytes;

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
                // Longest first: a shorter value that is a prefix of a longer one must not leave a fragment.
                .OrderByDescending(entry => entry.Value.Length),
        ];
        _names = [.. _values.Select(entry => entry.Name).Distinct(StringComparer.Ordinal)];
        _nameBytes = [.. _names.Select(Utf8NoBom.GetBytes)];
        _markers = [.. _values.Select(entry => MarkerPrefix + LabelFor(entry.Name) + "]")];
        _valueBytes = [.. _values.Select(entry => Utf8NoBom.GetBytes(entry.Value))];
        _markerBytes = [.. _markers.Select(Utf8NoBom.GetBytes)];
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
    /// in <paramref name="text"/> is passed through untouched only when its name names a credential
    /// this redactor knows and the span through <c>]</c> carries no known value; any other span
    /// falls through to value matching. Re-redacting is identity, and no known value survives
    /// anywhere in the output, not even inside a marker label.
    /// </summary>
    internal string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length);
        int index = 0;
        while (index < text.Length)
        {
            // A pre-existing marker is copied verbatim, but only when the bracketed name is a known
            // name and the span carries no known value; it otherwise falls through to value
            // matching, so text shaped like a marker cannot smuggle the raw secret through.
            if (text.AsSpan(index).StartsWith(MarkerPrefix, StringComparison.Ordinal))
            {
                int close = text.IndexOf(']', index + MarkerPrefix.Length);
                if (close > index + MarkerPrefix.Length)
                {
                    int nameStart = index + MarkerPrefix.Length;
                    int length = close + 1 - index;
                    if (
                        _names.Contains(text[nameStart..close], StringComparer.Ordinal)
                        && !ContainsValue(text.AsSpan(index, length))
                    )
                    {
                        builder.Append(text, index, length);
                        index = close + 1;
                        continue;
                    }
                }
            }

            // Values are longest first, so the first match here is the longest one at this index.
            bool matched = false;
            for (int entry = 0; entry < _values.Count; entry++)
            {
                if (text.AsSpan(index).StartsWith(_values[entry].Value, StringComparison.Ordinal))
                {
                    builder.Append(_markers[entry]);
                    index += _values[entry].Value.Length;
                    matched = true;
                    break;
                }
            }

            if (matched)
            {
                continue;
            }

            builder.Append(text[index]);
            index++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// The byte-level form, for the JSONL export, so a line without a credential stays exactly the
    /// bytes it was. The name-checked, marker-safe scan of <see cref="Redact"/> applies here too:
    /// no known value survives anywhere in the output, not even inside a marker label.
    /// </summary>
    internal byte[] RedactUtf8(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        byte[] prefixBytes = Utf8NoBom.GetBytes(MarkerPrefix);
        if (
            source.AsSpan().IndexOf(prefixBytes) < 0
            && _valueBytes.All(value => source.AsSpan().IndexOf(value) < 0)
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
                    if (
                        IsKnownName(remaining[prefixBytes.Length..close])
                        && !ContainsValue(remaining[..(close + 1)])
                    )
                    {
                        CopySpan(builder, remaining[..(close + 1)]);
                        index += close + 1;
                        continue;
                    }
                }
            }

            bool matched = false;
            for (int entry = 0; entry < _values.Count; entry++)
            {
                if (remaining.StartsWith(_valueBytes[entry]))
                {
                    CopySpan(builder, _markerBytes[entry]);
                    index += _valueBytes[entry].Length;
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

    /// <summary>
    /// The label of the marker an entry emits: the name, except when the name carries a known value
    /// as a substring — then the encoded form keeps the raw value out of the output.
    /// </summary>
    private string LabelFor(string name)
    {
        return ContainsValue(name)
            ? Convert.ToHexString(SHA256.HashData(Utf8NoBom.GetBytes(name))).ToLowerInvariant()[
                ..12
            ]
            : name;
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

    private static void CopySpan(List<byte> builder, ReadOnlySpan<byte> span)
    {
        foreach (byte b in span)
        {
            builder.Add(b);
        }
    }
}
