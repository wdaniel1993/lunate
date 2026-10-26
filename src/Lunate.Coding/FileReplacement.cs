namespace Lunate.Coding;

/// <summary>
/// Replaces a file that another thread or process may be reading (design: atomic writes). On
/// Windows a reader that did not open with delete sharing makes the replace fail with access-denied
/// for as long as it holds the handle, so the move is retried briefly; a write is short and the
/// reader closes in milliseconds. One implementation serves every replace Lunate makes over a file
/// it may share: <see cref="TextFile.WriteRaw"/> and the settings store.
/// </summary>
internal static class FileReplacement
{
    /// <summary>Five attempts with a growing pause: a settings or file write is short.</summary>
    private const int Attempts = 5;

    /// <summary>
    /// Moves <paramref name="source"/> onto <paramref name="destination"/>, retrying while a reader
    /// holds the destination. <paramref name="preserveTarget"/> replaces an existing target in place
    /// on Windows (<see cref="File.Replace(string, string, string?)"/> keeps its ACLs and
    /// attributes); on POSIX a move is a rename either way, so the caller copies the mode first.
    /// The last failure is rethrown unchanged, so each caller can word its own report.
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
