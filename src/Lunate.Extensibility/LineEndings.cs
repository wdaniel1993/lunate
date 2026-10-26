namespace Lunate.Extensibility;

/// <summary>
/// Line-ending normalization for every digest over content the user owns. A digest answers "is this
/// the same text?", so the line endings a checkout happens to carry — CRLF where <c>core.autocrlf</c>
/// is on, LF where it is not — must not change the answer. Only the pair CRLF is normalized: a lone
/// CR stays, because it is a byte in the text, not a line break this host understands.
/// </summary>
/// <remarks>
/// The normalization is byte-level and does not guess at the encoding: a file that carries the pair
/// 0x0D 0x0A is treated as text. Extension directories and single-file sources are source trees, so
/// that is the content the digest means to compare; a binary that happens to contain the pair is
/// normalized too, which can only make two files that differ solely in that pair hash alike — never
/// two files that differ in anything a compiler reads.
/// </remarks>
internal static class LineEndings
{
    /// <summary>
    /// <paramref name="bytes"/> with every CRLF replaced by LF; the same memory when there is
    /// nothing to replace.
    /// </summary>
    internal static ReadOnlySpan<byte> ToLf(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.Contains((byte)'\r'))
        {
            return bytes;
        }

        var normalized = new byte[bytes.Length];
        var length = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            if (
                bytes[index] == (byte)'\r'
                && index + 1 < bytes.Length
                && bytes[index + 1] == (byte)'\n'
            )
            {
                continue;
            }

            normalized[length++] = bytes[index];
        }

        return normalized.AsSpan(0, length);
    }
}
