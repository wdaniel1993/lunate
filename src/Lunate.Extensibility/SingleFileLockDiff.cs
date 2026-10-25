using System.Globalization;

namespace Lunate.Extensibility;

/// <summary>
/// The lines two locks are compared by, split by what a difference means. <see cref="Between"/>
/// reports the <b>graph</b> difference — packages, their hashes, the source bytes the resolution
/// came from — and only that asks the user, because only that changes what they approved. The
/// <b>toolset</b> difference is reported by <see cref="ToolsetBetween"/> as a rebuild reason: a
/// .NET patch must rebuild silently, never re-ask. Ordering is by id and the text is invariant, so
/// the same pair of locks always reads the same.
/// </summary>
public static class SingleFileLockDiff
{
    /// <summary>
    /// The trust-relevant change lines between <paramref name="existing"/> and
    /// <paramref name="resolved"/>; empty when they resolve the same graph. <paramref name="existing"/>
    /// null means a lock file that could not be read, which is reported as a replacement rather than
    /// as a diff. A difference in the toolset is deliberately not part of this list.
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

        if (!string.Equals(existing.SourceDigest, resolved.SourceDigest, StringComparison.Ordinal))
        {
            changes.Add(
                "~ resolved from different source bytes (the file changed since it was trusted)"
            );
        }

        return changes;
    }

    /// <summary>
    /// The toolset lines between <paramref name="existing"/> and <paramref name="resolved"/>: why a
    /// restore rebuilds the lock (and, through it, the build) without a trust question. Empty when the
    /// two name the same framework, RID, runtime and reference pack.
    /// </summary>
    public static IReadOnlyList<string> ToolsetBetween(
        ExtensionLock? existing,
        ExtensionLock resolved
    )
    {
        ArgumentNullException.ThrowIfNull(resolved);

        if (existing is null)
        {
            return [];
        }

        List<string> changes = [];
        Add(changes, "target framework", existing.Toolset.Tfm, resolved.Toolset.Tfm);
        Add(changes, "RID", existing.Toolset.Rid, resolved.Toolset.Rid);
        Add(changes, "runtime", existing.Toolset.Runtime, resolved.Toolset.Runtime);
        Add(
            changes,
            "reference pack",
            existing.Toolset.ReferencePack.Version,
            resolved.Toolset.ReferencePack.Version
        );
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
