using System.Security.Cryptography;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// What a record's content answers for now (spec: "The <c>lunate packages</c> view"): the recorded
/// paths whose bytes changed and the ones that are gone. A clean state is the only one in which a
/// record still describes the machine.
/// </summary>
internal sealed record PackageContentState(
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Missing
)
{
    public bool Clean => Changed.Count == 0 && Missing.Count == 0;
}

/// <summary>
/// Recomputes every recorded file's SHA-256 and size (design D8): the record says what was written,
/// and this is how the machine checks that it is still there and still those bytes. A file that
/// cannot be read counts as changed — it cannot be shown intact.
/// </summary>
internal static class PackageContentVerifier
{
    public static PackageContentState Verify(PackageInstallRecord record, string scopeRoot)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeRoot);

        List<string> changed = [];
        List<string> missing = [];
        foreach (InstalledFile file in record.Content.Paths)
        {
            string path = Path.Combine(
                scopeRoot,
                file.Path.Replace('/', Path.DirectorySeparatorChar)
            );
            if (!File.Exists(path))
            {
                missing.Add(file.Path);
                continue;
            }

            try
            {
                using FileStream stream = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete
                );
                byte[] hash = SHA256.HashData(stream);
                if (
                    stream.Length != file.Size
                    || !string.Equals(
                        Convert.ToHexStringLower(hash),
                        file.Sha256,
                        StringComparison.Ordinal
                    )
                )
                {
                    changed.Add(file.Path);
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                changed.Add(file.Path);
            }
        }

        return new PackageContentState(changed, missing);
    }
}
