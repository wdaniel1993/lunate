using System.Security.Cryptography;
using System.Text;

namespace Lunate.Extensibility;

/// <summary>
/// The single-file extension trust digest: <c>sha256(tag | id | every declared package as written
/// | source bytes including the header | lock bytes)</c>. The first trust has no lock yet, so its
/// digest is the same function with empty lock bytes; the first successful restore re-records it
/// with the bytes it wrote. The tag names the format, so a later digest layout cannot be mistaken
/// for this one.
/// </summary>
public static class SingleFileExtensionDigest
{
    /// <summary>The digest format tag; a format change is a new tag, never a silent reinterpretation.</summary>
    public const string FormatTag = "lunate-extension-v1";

    /// <summary>
    /// The first-trust form: the same layout with no lock bytes yet, because nothing has been
    /// restored. A restore that writes a lock re-records the full form without a second question.
    /// </summary>
    public static string ComputeSource(
        string id,
        IReadOnlyList<ExtensionPackageReference> packages,
        ReadOnlySpan<byte> source
    ) => Compute(id, packages, source, []);

    /// <summary>
    /// The digest of the bytes the user approved: fields NUL-separated, the source appended
    /// verbatim and the lock bytes last (empty before the first restore). NUL is unambiguous
    /// because neither an id nor a package argument can contain it.
    /// </summary>
    public static string Compute(
        string id,
        IReadOnlyList<ExtensionPackageReference> packages,
        ReadOnlySpan<byte> source,
        ReadOnlySpan<byte> lockBytes
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
        hash.AppendData(separator);
        hash.AppendData(lockBytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
