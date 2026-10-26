namespace Lunate.Ai;

/// <summary>
/// An exclusive claim on a file, held by an open handle: the one way a Lunate file that two
/// processes may touch says "one at a time". The claim is a lock file of its own beside the data,
/// because a claim on the data file would also refuse the readers that never write it.
/// <para>
/// The handle is opened with <see cref="FileShare.None"/>, the one share mode Windows and POSIX both
/// refuse to hand out twice — Windows through the create call's sharing rule, POSIX through the
/// advisory lock the runtime takes for it. A claim held by this process refuses a second one from
/// this process too, which is why <see cref="Acquire"/> waits instead of assuming a refusal is
/// always another process's.
/// </para>
/// </summary>
internal sealed class FileLock : IDisposable
{
    private readonly FileStream _handle;

    private FileLock(FileStream handle) => _handle = handle;

    /// <summary>
    /// The claim on <paramref name="lockPath"/>, or null when someone else holds it. A file that
    /// cannot be created at all (a read-only directory) throws instead: that is not contention, and
    /// reporting it as "busy" would send the caller into a wait that cannot end.
    /// </summary>
    internal static FileLock? TryAcquire(string lockPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);
        try
        {
            return new FileLock(Open(lockPath));
        }
        catch (IOException) when (File.Exists(lockPath))
        {
            return null;
        }
    }

    /// <summary>
    /// The claim on <paramref name="lockPath"/>, waiting up to <paramref name="timeout"/> for its
    /// current holder. A caller that must not run without the claim gets a
    /// <see cref="TimeoutException"/> naming the file instead of a claim it does not hold.
    /// </summary>
    internal static FileLock Acquire(string lockPath, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (true)
        {
            if (TryAcquire(lockPath) is { } held)
            {
                return held;
            }

            if (Environment.TickCount64 >= deadline)
            {
                throw new TimeoutException(
                    FormattableString.Invariant(
                        $"the lock file '{lockPath}' stayed claimed for {timeout.TotalMilliseconds:0} ms."
                    )
                );
            }

            Thread.Sleep(PollInterval);
        }
    }

    /// <summary>Long enough not to spin, short enough that a held write is not felt as a stall.</summary>
    private const int PollInterval = 10;

    public void Dispose() => _handle.Dispose();

    private static FileStream Open(string lockPath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(lockPath));
        if (!string.IsNullOrEmpty(directory))
        {
            SecureFiles.CreateOwnerOnlyDirectory(directory);
        }

        FileStream stream = SecureFiles.OpenOwnerOnly(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
        SecureFiles.RestrictToOwner(lockPath);
        return stream;
    }
}
