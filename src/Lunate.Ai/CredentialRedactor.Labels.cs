using System.Security.Cryptography;

namespace Lunate.Ai;

/// <summary>Label derivation for <see cref="CredentialRedactor"/>.</summary>
internal sealed partial class CredentialRedactor
{
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
}
