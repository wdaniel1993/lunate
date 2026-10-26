namespace Lunate.Agent;

/// <summary>
/// The one writer a session file admits (spec: "The session file"). The first append claims an
/// exclusive handle on the file's lock, and another process is refused from then on: two processes
/// appending to one session mint duplicate entry ids and parents that name the wrong entry, and
/// nothing in the file can repair that afterwards. A load claims nothing — reading a session is
/// how a picker, a summary and a child mirror all see it while it is being written.
/// <para>
/// A second claim from this process is adopted rather than refused. A session is single-writer here
/// by construction — one harness owns it — while the resume, mirror and test paths legitimately
/// hold one file through two objects, and Windows and POSIX disagree about which of those an
/// exclusive handle should refuse. The claim belongs to the process for its lifetime, so a session
/// that is closed and re-opened in one process keeps the file it had claimed.
/// </para>
/// </summary>
internal static class SessionWriterClaims
{
    private static readonly object Gate = new();

    // Process-lifetime claims, never released: the handle is the claim, and closing it would let
    // another process in while this one still appends. One handle per session file this process
    // writes, which is a handful in a session and bounded by the suite in a test run.
    private static readonly Dictionary<string, FileStream> Claims = new(StringComparer.Ordinal);

    /// <summary>
    /// Claims <paramref name="path"/> for this process, or refuses when another process holds it.
    /// Idempotent: a path already claimed here is a no-op.
    /// </summary>
    internal static void Claim(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string key = Path.GetFullPath(path);
        lock (Gate)
        {
            if (Claims.ContainsKey(key))
            {
                return;
            }

            Claims[key] = SessionFiles.OpenWriterLock(path);
        }
    }
}
