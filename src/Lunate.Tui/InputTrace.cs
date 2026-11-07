using System.Globalization;
using System.Text;

namespace Lunate.Tui;

/// <summary>
/// The opt-in key-event trace behind <c>LUNATE_INPUT_TRACE</c>: evidence for a report whose keys
/// never reached the session — an <c>Esc</c> held back by a curse timer, a paste swallowed by the
/// paste bridge. It records key kinds, modifiers and counts, never characters: printable text and
/// paste content are reduced to <c>chars=N</c>, so a credential typed into a prompt leaves nothing
/// in the file. Timestamps are UTC; the file is UTF-8 without a BOM with LF line endings, so its
/// bytes do not depend on the machine that wrote it.
/// <para>
/// <c>LUNATE_INPUT_TRACE=1</c> traces to <c>diagnostics/input-trace.log</c> under the state root;
/// any other value must name a path inside the state root and is refused otherwise — a diagnostic
/// the user can enable writes where the user's records live, never where a repository or a temp
/// drive does. On POSIX the file is owner-only. The same rule holds a second copy over in
/// <c>Lunate.Coding</c> because the assemblies are independent; the boundary tests pin it.
/// </para>
/// </summary>
internal static class InputTrace
{
    internal const string Variable = "LUNATE_INPUT_TRACE";
    internal const string EnabledValue = "1";
    internal const string HomeVariable = "LUNATE_HOME";

    /// <summary>UTF-8 without a BOM; the AppendAllText text already ends in its LF.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Traces one decoded key. The terminal half of the trace, before the session is involved.
    /// </summary>
    public static void Key(KeyEvent key)
    {
        string? path = TracePath();
        if (path is null)
        {
            return;
        }

        Key(key, path);
    }

    /// <summary>Records the key at an already-resolved path; the formatting under test.</summary>
    internal static void Key(KeyEvent key, string path)
    {
        Write(
            path,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTimeOffset.UtcNow:O} key kind={key.Kind} ctrl={key.Ctrl} shift={key.Shift} alt={key.Alt} chars={Chars(key)}"
            )
        );
    }

    /// <summary>A counted value: how printable characters a key carries, or "-" when none.</summary>
    internal static string Chars(KeyEvent key) =>
        key.Text is null ? "-" : key.Text.Length.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Resolves the trace path for a setting against a state root, or null when the setting is
    /// off. <c>1</c> takes the default file; a relative value resolves against the state root
    /// (so <c>diagnostics/x.log</c> is eligible) and a value that escapes it is refused.
    /// </summary>
    internal static string? Resolve(string? setting, string stateRoot)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            return null;
        }

        if (setting == EnabledValue)
        {
            return Path.Combine(stateRoot, "diagnostics", "input-trace.log");
        }

        string candidate = Path.GetFullPath(setting, stateRoot);
        return IsInside(Canonicalize(candidate), Canonicalize(stateRoot)) ? candidate : null;
    }

    /// <summary>
    /// Writes one line. A failed trace never disturbs input: the diagnostic's failure is nothing.
    /// </summary>
    internal static void Write(string path, string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, line + '\n', Utf8);
            if (!OperatingSystem.IsWindows())
            {
                // Every write, not once per process: a file written through another path must not
                // keep its create-mode permissions.
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or IOException
                        or NotSupportedException
            ) { }
    }

    private static string? TracePath()
    {
        string? setting = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(setting))
        {
            return null;
        }

        return Resolve(setting, StateRoot());
    }

    /// <summary>
    /// The state root. Same rule as <c>LunateHome</c>'s: the profile default or the absolute
    /// <c>LUNATE_HOME</c> override, read from the process environment and nowhere else.
    /// </summary>
    private static string StateRoot()
    {
        string? configured = Environment.GetEnvironmentVariable(HomeVariable);
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".lunate"
            )
            : Path.TrimEndingDirectorySeparator(configured);
    }

    private static bool IsInside(string candidate, string boundary)
    {
        if (string.Equals(candidate, boundary, StringComparison))
        {
            return true;
        }

        string prefix = Path.EndsInDirectorySeparator(boundary)
            ? boundary
            : boundary + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison);
    }

    // Mirrors PathCanonical in Lunate.Contracts: every existing component's symlink resolved, so
    // /var/… and /private/var/… are one path on macOS. Dangling links resolve to their target.
    private static string Canonicalize(string path) => Canonicalize(path, depth: 0);

    private static string Canonicalize(string path, int depth)
    {
        if (depth > 40)
        {
            throw new IOException($"too many levels of symbolic links resolving '{path}'");
        }

        var full = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
        {
            return full;
        }

        var current = root;
        foreach (
            string segment in full[root.Length..]
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries
                )
        )
        {
            string next = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(next)
                ? new DirectoryInfo(next)
                : new FileInfo(next);
            current = info.LinkTarget is { } target
                ? Canonicalize(
                    Path.IsPathRooted(target)
                        ? target
                        : Path.Combine(Path.GetDirectoryName(next)!, target),
                    depth + 1
                )
                : next;
        }

        return current;
    }

    /// <summary>Windows and macOS compare case-insensitively by default; Linux does not.</summary>
    private static StringComparison StringComparison =>
        OperatingSystem.IsLinux()
            ? global::System.StringComparison.Ordinal
            : global::System.StringComparison.OrdinalIgnoreCase;
}
