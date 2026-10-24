using System.Globalization;

namespace Lunate.Extensibility;

/// <summary>
/// The lines a "this restore would change the lock" question shows. It compares the resolution a
/// restore produces with the lock already on disk — package by package, then the toolset — so the
/// user sees exactly what would change instead of a bare "the lock is different". Ordering is by id
/// and the text is invariant, so the same pair of locks always reads the same.
/// </summary>
public static class SingleFileLockDiff
{
    /// <summary>
    /// The change lines between <paramref name="existing"/> and <paramref name="resolved"/>; empty
    /// when they say the same thing. <paramref name="existing"/> null means a lock file that could
    /// not be read, which is reported as a replacement rather than as a diff.
    /// </summary>
    public static IReadOnlyList<string> Between(ExtensionLock? existing, ExtensionLock resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        if (existing is null)
        {
            return ["the lock file on disk could not be read and would be replaced"];
        }

        List<string> changes = [];
        Dictionary<string, ExtensionLockPackage> before = Index(existing.Packages);
        Dictionary<string, ExtensionLockPackage> after = Index(resolved.Packages);

        foreach (
            string id in after
                .Keys.Union(before.Keys, StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
        )
        {
            if (!before.TryGetValue(id, out ExtensionLockPackage? old))
            {
                changes.Add(FormattableString.Invariant($"+ {after[id].Id} {after[id].Version}"));
            }
            else if (!after.TryGetValue(id, out ExtensionLockPackage? now))
            {
                changes.Add(FormattableString.Invariant($"- {old.Id} {old.Version}"));
            }
            else if (!string.Equals(old.Version, now.Version, StringComparison.Ordinal))
            {
                changes.Add(
                    FormattableString.Invariant($"~ {now.Id} {old.Version} -> {now.Version}")
                );
            }
            else if (!string.Equals(old.Sha512, now.Sha512, StringComparison.Ordinal))
            {
                changes.Add(
                    FormattableString.Invariant($"~ {now.Id} {now.Version} (package hash)")
                );
            }
        }

        Add(changes, "target framework", existing.Toolset.Tfm, resolved.Toolset.Tfm);
        Add(changes, "RID", existing.Toolset.Rid, resolved.Toolset.Rid);
        Add(changes, "runtime", existing.Toolset.Runtime, resolved.Toolset.Runtime);
        Add(
            changes,
            "reference pack",
            existing.Toolset.ReferencePack.Version,
            resolved.Toolset.ReferencePack.Version
        );
        if (!string.Equals(existing.SourceDigest, resolved.SourceDigest, StringComparison.Ordinal))
        {
            changes.Add(
                "~ resolved from different source bytes (the file changed since it was trusted)"
            );
        }

        return changes;
    }

    private static Dictionary<string, ExtensionLockPackage> Index(
        IReadOnlyList<ExtensionLockPackage> packages
    )
    {
        Dictionary<string, ExtensionLockPackage> index = new(StringComparer.OrdinalIgnoreCase);
        foreach (ExtensionLockPackage package in packages)
        {
            index[package.Id] = package;
        }

        return index;
    }

    private static void Add(List<string> changes, string what, string before, string after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            changes.Add(FormattableString.Invariant($"~ {what} {before} -> {after}"));
        }
    }
}
