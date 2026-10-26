using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lunate.Coding;

/// <summary>
/// The serializer every hashed document in this assembly goes through. <c>NewLine</c> is pinned
/// because the default follows <see cref="Environment.NewLine"/>: the same pending decisions would
/// be two different byte sequences on Windows and POSIX, and the digest covers exactly those bytes.
/// With indentation off the pin is invisible today; it is what keeps the digest still if the
/// document is ever written indented.
/// </summary>
internal static class CanonicalJson
{
    internal static JsonSerializerOptions Options { get; } = new() { NewLine = "\n" };

    /// <summary>The canonical text of one document: invariant, unindented, one line.</summary>
    internal static string Text(JsonNode node) => node.ToJsonString(Options);

    /// <summary>SHA-256, hex lower, of the canonical text's UTF-8 bytes without a byte-order mark.</summary>
    internal static string Hash(JsonNode node) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Text(node))));
}
