using System.Text;

namespace Lunate.Ai;

/// <summary>
/// Replaces the credential values Lunate itself holds with <c>[redacted:&lt;name&gt;]</c> at every
/// boundary where content leaves the live process: a session file, a spill file, an export; the
/// agent loop applies the same redactor on the way into the context. Replacement is by exact value
/// only (no "looks like a key" patterns) over what Lunate knows: the auth store, the provider
/// environment variables, the extension secret files and the MCP server environment values of the
/// session's config files; <c>Lunate.Coding</c> collects them and this one implementation serves
/// both hosts. Invariant: no known value remains anywhere in the output — not as itself, not as a
/// substring, not inside a marker label. Each entry emits one precomputed marker labelled with its
/// name; a name that contains a known value gets an encoded label (first 12 lowercase hex
/// characters of SHA-256 over the name's UTF-8 bytes) that resolves back through
/// <see cref="CredentialNameForLabel"/>. Values shorter than <see cref="MinimumValueLength"/> are
/// never known values.
/// The scan is global, in passes: keep the marker-shaped spans that qualify for verbatim
/// pass-through, enumerate every value occurrence outside them, keep the longest disjoint matches
/// (ties by earlier start, then entry order) and emit — every match is accepted, broken by a
/// winning span, or lies in a gap holding no full match, so re-redacting is identity.
/// </summary>
internal sealed partial class CredentialRedactor
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
    private readonly Dictionary<string, string> _nameForLabel;

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
        // Plain and encoded labels alike name the entry that emitted them; first wins.
        _nameForLabel = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string name, _) in _values)
        {
            _nameForLabel.TryAdd(LabelFor(name), name);
        }
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
    /// The credential a placeholder label refers to: a literal credential name maps to itself, an
    /// encoded label to the entry that emitted it (first on the improbable encoding collision),
    /// anything else to null.
    /// </summary>
    internal string? CredentialNameForLabel(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (_names.Contains(label, StringComparer.Ordinal))
        {
            return label;
        }

        return _nameForLabel.TryGetValue(label, out string? name) ? name : null;
    }

    /// <summary>
    /// The text with every known credential value replaced by its marker, overlaps resolved
    /// globally with the longest match winning. A marker already present in
    /// <paramref name="text"/> is copied verbatim only when the bracketed label is a known
    /// credential name and the span through <c>]</c> carries no known value; any other
    /// marker-shaped span falls through to value matching.
    /// </summary>
    internal string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (
            !text.Contains(MarkerPrefix, StringComparison.Ordinal)
            && _values.All(entry => !text.Contains(entry.Value, StringComparison.Ordinal))
        )
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        int position = 0;
        foreach ((int start, int end, int entry) in EmitPlan(ValueMatches(text), MarkerSpans(text)))
        {
            builder.Append(text, position, start - position);
            if (entry == Protected)
            {
                builder.Append(text, start, end - start);
            }
            else
            {
                builder.Append(_markers[entry]);
            }

            position = end;
        }

        return builder.Append(text[position..]).ToString();
    }

    /// <summary>
    /// The byte-level form, for the JSONL export, so a line without a credential stays exactly the
    /// bytes it was. Overlap resolution is global, as in <see cref="Redact"/>.
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

        var builder = new List<byte>(source.Length);
        int position = 0;
        foreach (
            (int start, int end, int entry) in EmitPlan(
                ValueMatches(source),
                MarkerSpans(source, prefixBytes)
            )
        )
        {
            CopySpan(builder, source[position..start]);
            CopySpan(builder, entry == Protected ? source[start..end] : _markerBytes[entry]);
            position = end;
        }

        CopySpan(builder, source[position..]);
        return [.. builder];
    }

    /// <summary>The marker the session's export writes in place of a known value.</summary>
    internal static string Marker(string name) => "[redacted:" + name + "]";

    private const string MarkerPrefix = "[redacted:";

    /// <summary>A protected span in the emission plan carries this instead of an entry index.</summary>
    private const int Protected = -1;

    private static void CopySpan(List<byte> builder, ReadOnlySpan<byte> span)
    {
        foreach (byte b in span)
        {
            builder.Add(b);
        }
    }
}
