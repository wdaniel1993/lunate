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

    /// <summary>The text with every known credential value replaced by its marker.</summary>
    internal string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string redacted = text;
        foreach ((string name, string value) in _values)
        {
            if (redacted.Contains(value, StringComparison.Ordinal))
            {
                redacted = redacted.Replace(value, Marker(name), StringComparison.Ordinal);
            }
        }

        return redacted;
    }

    /// <summary>
    /// The byte-level form, for the JSONL export: the session file is edited in place byte for
    /// byte, so a line without a credential stays exactly the bytes it was.
    /// </summary>
    internal byte[] RedactUtf8(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        byte[] redacted = source;
        foreach ((string name, string value) in _values)
        {
            byte[] pattern = Utf8NoBom.GetBytes(value);
            if (redacted.AsSpan().IndexOf(pattern) < 0)
            {
                continue;
            }

            redacted = ReplaceBytes(redacted, pattern, Utf8NoBom.GetBytes(Marker(name)));
        }

        return redacted;
    }

    /// <summary>The marker the session's export writes in place of a known value.</summary>
    internal static string Marker(string name) => "[redacted:" + name + "]";

    private static byte[] ReplaceBytes(byte[] source, byte[] pattern, byte[] replacement)
    {
        var builder = new List<byte>(source.Length);
        var index = 0;
        while (index < source.Length)
        {
            int match = source.AsSpan(index).IndexOf(pattern);
            if (match < 0)
            {
                builder.AddRange(source.AsSpan(index).ToArray());
                break;
            }

            builder.AddRange(source.AsSpan(index, match).ToArray());
            builder.AddRange(replacement);
            index += match + pattern.Length;
        }

        return [.. builder];
    }
}
