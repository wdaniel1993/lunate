using System.Security.Cryptography;
using System.Text;

namespace Lunate.Extensibility;

/// <summary>
/// The single-file extension trust digest. In this phase there is no lock, so the digest covers
/// what the user actually approved: the id, every declared package exactly as written (a version
/// range included) and the source bytes including the header. The tag names the format, so a later
/// digest that also covers the lock bytes cannot be mistaken for this one.
/// </summary>
public static class SingleFileExtensionDigest
{
    /// <summary>The digest format tag; a format change is a new tag, never a silent reinterpretation.</summary>
    public const string FormatTag = "lunate-extension-v1";

    /// <summary>
    /// <c>sha256(tag | id | each package as written | source)</c>, fields NUL-separated and the
    /// source appended verbatim. NUL is unambiguous because neither an id nor a package argument
    /// can contain it.
    /// </summary>
    public static string Compute(
        string id,
        IReadOnlyList<ExtensionPackageReference> packages,
        ReadOnlySpan<byte> source
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(packages);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] separator = [0];
        hash.AppendData(Encoding.UTF8.GetBytes(FormatTag));
        hash.AppendData(separator);
        hash.AppendData(Encoding.UTF8.GetBytes(id));
        hash.AppendData(separator);
        foreach (ExtensionPackageReference package in packages)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(package.Written));
            hash.AppendData(separator);
        }

        hash.AppendData(source);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
