namespace Lunate.Ai;

/// <summary>
/// The owner-only half of a file's contract: session directories, session files, spill files, the
/// trust store and install records hold credentials, conversation content and the decisions that
/// govern what may run. On POSIX each is created with an explicit mode instead of the process umask.
/// </summary>
internal static class SecureFiles
{
    /// <summary>A file only its owner can read and write (0600).</summary>
    internal const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>A directory only its owner can enter (0700).</summary>
    internal const UnixFileMode OwnerOnlyDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// Creates a directory and every missing parent, then narrows the leaf to 0700 on POSIX.
    /// </summary>
    /// <remarks>
    /// Windows has no mode bits. There the file's protection is inherited from the directory it is
    /// created in: <c>%USERPROFILE%\.lunate</c> sits under the user profile, whose discretionary ACL
    /// grants the signed-in account (and SYSTEM/Administrators) and denies every other user, so the
    /// session, spill, trust and record files inside it are not readable by another account. Nothing
    /// here loosens that, and no separate ACL is written because the inherited one already is
    /// user-only: writing a narrower one would fight the profile's own inheritance without adding
    /// protection.
    /// </remarks>
    internal static void CreateOwnerOnlyDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        bool existed = Directory.Exists(path);
        Directory.CreateDirectory(path);
        if (!existed && !OperatingSystem.IsWindows())
        {
            // Only a directory this call created is narrowed: tightening a directory the user
            // pointed a session at is their decision, and the files inside stay 0600 regardless.
            File.SetUnixFileMode(path, OwnerOnlyDirectory);
        }
    }

    /// <summary>
    /// Narrows an existing file to 0600 on POSIX; a no-op on Windows, where the profile's inherited
    /// ACL is the protection (see <see cref="CreateOwnerOnlyDirectory"/>).
    /// </summary>
    internal static void RestrictToOwner(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, OwnerOnlyFile);
        }
    }

    /// <summary>
    /// Opens a stream that creates a file 0600 on POSIX and lets the profile's ACL decide on Windows.
    /// <c>UnixCreateMode</c> applies only when the file is created, so an existing file keeps the
    /// mode <see cref="RestrictToOwner"/> already gave it.
    /// </summary>
    internal static FileStream OpenOwnerOnly(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare share
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var options = new FileStreamOptions
        {
            Mode = mode,
            Access = access,
            Share = share,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnlyFile;
        }

        return new FileStream(path, options);
    }
}
