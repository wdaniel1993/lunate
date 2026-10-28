namespace Lunate.Coding;

/// <summary>
/// Replaces a file that another thread or process may be reading (design: atomic writes). One
/// implementation serves every replace Lunate makes over a file it may share:
/// <see cref="TextFile.WriteRaw"/> and the settings store.
/// <para>
/// On Windows the two replacements differ, and only one of them survives a live reader. Renaming
/// over the destination (<see cref="File.Move(string, string, bool)"/> with overwrite) fails with
/// access-denied for as long as any handle holds it, delete sharing included, and the pause the
/// retry adds cannot outlast a reader that keeps reopening the file.
/// <see cref="File.Replace(string, string, string?)"/> replaces the target in place instead: it
/// succeeds while a reader holds it with delete sharing, and fails with a sharing violation while
/// one holds it without — which is why the settings reader opens with
/// <see cref="FileShare.Delete"/>. Pass <paramref name="preserveTarget"/> for any target a reader
/// may hold; it also keeps the target's ACLs and attributes.
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
    /// ACLs and attributes, and is the only one of the two that a live reader does not block); on
    /// POSIX a move is a rename either way, so the caller copies the mode first. The last failure
    /// is rethrown unchanged, so each caller can word its own report.
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
