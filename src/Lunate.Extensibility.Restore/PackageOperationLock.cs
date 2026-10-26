using Lunate.Ai;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// The one mutating operation a package admits (design D6). An install and an uninstall both take
/// this claim before they read the record and hold it until they are done with the content, so no
/// run stages into, commits over or records under another one's half-finished work.
/// <para>
/// The claim is keyed by the canonical record path — one record per package id, whatever spelling
/// the command was given — and lives under <c>&lt;root&gt;/packages/locks/</c> rather than in the
/// package's own directory, which an uninstall removes.
/// </para>
/// </summary>
internal static class PackageOperationLock
{
    /// <summary>
    /// How long an operation waits for the package's claim. Long enough for a download plus a
    /// commit — an install holds it for its whole run — and short enough that a stuck holder is
    /// reported rather than waited on forever.
    /// </summary>
    private static readonly TimeSpan Wait = TimeSpan.FromMinutes(10);

    /// <summary>The lock file that guards <paramref name="recordPath"/> and the content it names.</summary>
    internal static string PathFor(string recordPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordPath);

        string recordDirectory =
            Path.GetDirectoryName(Path.GetFullPath(recordPath))
            ?? throw new InvalidOperationException($"'{recordPath}' is not a record file path.");
        // The record directory is <root>/packages/<package-id>/, so its parent holds every package's
        // directory and is where the claims live, beside them but never inside one.
        string packages =
            Path.GetDirectoryName(recordDirectory)
            ?? throw new InvalidOperationException(
                $"the record path '{recordPath}' is not under a package directory."
            );

        return Path.Combine(packages, "locks", Path.GetFileName(recordDirectory) + ".lock");
    }

    /// <summary>
    /// Claims <paramref name="recordPath"/> for this operation, waiting for another one to finish.
    /// A claim that cannot be taken is a refusal: this command must not write the content and the
    /// record the other one is still producing.
    /// </summary>
    internal static FileLock Acquire(string recordPath, string packageId)
    {
        string lockPath = PathFor(recordPath);
        try
        {
            return FileLock.Acquire(lockPath, Wait);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException(
                FormattableString.Invariant(
                    $"package '{packageId}' is being installed or uninstalled by another Lunate process (lock '{lockPath}'), and it did not finish within {Wait.TotalMinutes:0} minutes; nothing was changed, so run the command again once that one has finished."
                ),
                exception
            );
        }
    }
}
