namespace Lunate.Coding;

/// <summary>
/// Replaces a file that another thread or process may be reading (design: atomic writes). One
/// implementation serves every replace Lunate makes over a file it may share:
/// <see cref="TextFile.WriteRaw"/> and the settings store.
/// <para>
/// On Windows the two replacements cost different things, measured on a windows-latest runner.
/// Renaming over the destination (<see cref="File.Move(string, string, bool)"/> with overwrite)
/// keeps the name continuously present, which a reader needs, but fails with access-denied for as
/// long as any handle holds the destination — delete sharing included — so the retry below only
/// outlasts a brief lock, never a reader. <see cref="File.Replace(string, string, string?)"/>
/// survives a held handle that shares delete, but it takes the destination's name away while it
/// works: a reader racing it sees a missing file. So a caller that a reader may race must keep the
/// two sides from overlapping in the first place (the settings store holds a gate) and leave the
/// retry to transient locks; <paramref name="preserveTarget"/> is for callers whose reader is not
/// this process.
/// </para>
/// </summary>
internal static class FileReplacement
{
    /// <summary>Five attempts with a growing pause: a write is short and a transient lock brief.</summary>
    private const int Attempts = 5;

    /// <summary>
    /// Moves <paramref name="source"/> onto <paramref name="destination"/>, retrying while the
    /// destination is transiently locked. <paramref name="preserveTarget"/> replaces an existing
    /// target in place on Windows (<see cref="File.Replace(string, string, string?)"/> keeps its
    /// ACLs and attributes); on POSIX a move is a rename either way, so the caller copies the mode
    /// first. The last failure is rethrown unchanged, so each caller can word its own report.
    /// </summary>
    internal static void Replace(string source, string destination, bool preserveTarget = false)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (preserveTarget && OperatingSystem.IsWindows() && File.Exists(destination))
                {
                    File.Replace(source, destination, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(source, destination, overwrite: true);
                }

                return;
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt >= Attempts)
                {
                    throw;
                }

                Thread.Sleep(20 * attempt);
            }
        }
    }
}
