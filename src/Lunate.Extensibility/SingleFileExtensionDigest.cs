using System.Security.Cryptography;
using System.Text;

namespace Lunate.Extensibility;

/// <summary>
/// The single-file extension trust digest. The first trust has no lock yet, so it covers
/// <c>sha256(tag | id | every declared package as written | source bytes including the header)</c>.
/// Once a restore has written a lock, the digest also covers the lock's <b>graph</b> half — the
/// packages, versions, hashes, assets and edges — under a second tag, so a resolution change is a
/// change to the approved bytes. The lock's <b>toolset</b> half is deliberately not an input: a
/// .NET patch moves the runtime and the reference pack, and asking the user about that would be a
/// question with no decision in it.
/// </summary>
public static class SingleFileExtensionDigest
{
    /// <summary>
    /// The source-only form's tag: the source-level identity a lock records (and the build key's
    /// <c>sourceHash</c>), not the trust identity of a file. Its layout is unchanged since the first
    /// release, so existing locks keep parsing; a file's trust digest is always the graph-bound form
    /// (<see cref="ComputeUnrestored"/> before a restore, <see cref="Compute"/> after one).
    /// </summary>
    public const string FormatTag = "lunate-extension-v1";

    /// <summary>
    /// The graph-bound form's tag: the source-only layout plus the lock's graph bytes. A distinct
    /// tag, so the two layouts can never be mistaken for one another.
    /// </summary>
    public const string GraphFormatTag = "lunate-extension-v2";

    /// <summary>
    /// The first-trust form, before anything has been restored: the id, the packages as written and
    /// the source bytes. A restore that writes a lock re-records the graph-bound form without a
    /// second question.
    /// </summary>
    public static string ComputeSource(
        string id,
        IReadOnlyList<ExtensionPackageReference> packages,
        ReadOnlySpan<byte> source
    )
    {
        using IncrementalHash hash = Start(FormatTag, id, packages, source);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// The digest a file that has never been restored has: the graph-bound layout with no graph
    /// segment, which is the value discovery computes and therefore the value a first trust records.
    /// It is what <see cref="ComputeSource"/> is not — that one keeps the older tag and is the
    /// source-level identity a lock records, not the trust identity of the file.
    /// </summary>
    public static string ComputeUnrestored(
        string id,
        IReadOnlyList<ExtensionPackageReference> packages,
        ReadOnlySpan<byte> source
    ) => Compute(id, packages, source, []);

    /// <summary>
    /// The digest of the bytes the user approved: fields NUL-separated, the source appended
    /// verbatim, and the lock's canonical graph bytes last. NUL is unambiguous because neither an
    /// id nor a package argument can contain it.
    /// </summary>
    public static string Compute(
        string id,
        IReadOnlyList<ExtensionPackageReference> packages,
        ReadOnlySpan<byte> source,
        ReadOnlySpan<byte> graphBytes
    )
    {
        ArgumentNullException.ThrowIfNull(packages);

        using IncrementalHash hash = Start(GraphFormatTag, id, packages, source);
        hash.AppendData(graphBytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static IncrementalHash Start(
        string tag,
        string id,
        IReadOnlyList<ExtensionPackageReference> packages,
        ReadOnlySpan<byte> source
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(packages);

        IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] separator = [0];
        hash.AppendData(Encoding.UTF8.GetBytes(tag));
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
        return hash;
    }
}
