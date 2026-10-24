namespace Lunate.Agent;

/// <summary>
/// Creates a session at its store-assigned name: the header is written to a provisional file and
/// the file is then renamed to <c>&lt;sessionId&gt;.jsonl</c>.
/// </summary>
/// <remarks>
/// Windows refuses the rename while any handle on the provisional file is still open, and it
/// refuses the reload that follows while a writer holds the renamed file. Both constraints are the
/// reason the header write returns before the move and the move completes before the load; POSIX
/// would let either order pass, which is why the failure only appeared on windows-latest.
/// </remarks>
internal static class SessionProvisioning
{
    /// <summary>
    /// Creates a session through <paramref name="create"/> in <paramref name="directory"/>, renames
    /// it to its id-named file and reloads it. A provisional file that never reached its final name
    /// is removed, so a failed creation leaves nothing in the sessions directory.
    /// </summary>
    internal static Session Create(string directory, Func<string, Session> create)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(create);

        Directory.CreateDirectory(directory);
        string provisional = Path.Combine(directory, Path.GetRandomFileName());
        bool renamed = false;
        try
        {
            Session created = create(provisional);
            string path = Path.Combine(directory, SessionPaths.SessionFileName(created.SessionId));
            File.Move(provisional, path);
            renamed = true;
            return Session.Load(path);
        }
        finally
        {
            if (!renamed)
            {
                TryDelete(provisional);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
