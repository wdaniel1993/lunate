using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>
/// The targets the bash guard protects: the state root that holds trust records, sessions and
/// settings, the install prefix that holds the <c>lunate</c> binary, and the credential file that
/// is additionally read-denied. <see cref="ProfileBase"/> is the directory the shell's <c>~</c>
/// spellings and the Landlock walk expand against.
/// </summary>
internal sealed record GuardTarget(
    string StateRoot,
    string AuthPath,
    string Prefix,
    string ProfileBase
)
{
    /// <summary>The live targets: <c>LunateHome.Store</c>, the directory of the running binary and
    /// the process's profile directory.</summary>
    internal static GuardTarget Resolve() =>
        new(
            LunateHome.Store,
            Path.Combine(LunateHome.Store, "auth.json"),
            InstallPrefix(),
            LunateHome.Profile
        );

    /// <summary>The install prefix: the directory that holds the running binary, so a guarded
    /// command cannot replace the launcher or the binary with its own.</summary>
    private static string InstallPrefix()
    {
        string binary = string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? AppContext.BaseDirectory
            : Environment.ProcessPath;
        string directory =
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(binary)) ?? LunateHome.Profile;
        return string.IsNullOrWhiteSpace(directory) ? LunateHome.Profile : directory;
    }

    /// <summary>Whether <paramref name="path"/> is, or lies beneath, a denied target.</summary>
    internal bool IsDenied(string path) =>
        LunateHome.IsInside(path, StateRoot) || LunateHome.IsInside(path, Prefix);

    /// <summary>Whether <paramref name="path"/> is, or lies beneath, the credential file — the
    /// one target deniable at file level, read and write both.</summary>
    internal bool IsDeniedFile(string path) => LunateHome.IsInside(path, AuthPath);

    /// <summary>Whether a denied target lies at or beneath <paramref name="path"/>.</summary>
    internal bool ContainsDenied(string path) =>
        LunateHome.IsInside(StateRoot, path) || LunateHome.IsInside(Prefix, path);
}

/// <summary>
/// One resolved guard: what the <c>bash</c> tool actually spawns and why, or (when no mechanism is
/// available) the unwrapped shell plus a notice the tool result must not swallow.
/// </summary>
/// <param name="Wrapped">True when the shell runs inside a guard mechanism.</param>
internal sealed record GuardCommand(
    string FileName,
    IReadOnlyList<string> ArgumentsPrefix,
    string Mechanism,
    bool Wrapped,
    string? Notice
);

/// <summary>
/// The bash guard: an OS-level deny, not a jail. It stops a bash command from writing under the
/// state root (<c>LUNATE_HOME</c>) or the install prefix, and from reading <c>auth.json</c>, while
/// everything else runs normally; a normal command pays only the wrapping.
/// <para>
/// Mechanisms per platform: <c>sandbox-exec</c> on macOS, <c>bwrap</c> on Linux, with
/// <c>setpriv</c>'s Landlock rules as the Linux fallback when user namespaces do not work, and
/// nothing on Windows (unsupported, and reported as such). Every resolution also runs the
/// mechanism once, so "installed" cannot stand in for "working"; a failed probe is reported, and
/// an AppArmor user-namespace restriction is detected and named, never assumed away.
/// </para>
/// </summary>
internal sealed partial class BashGuard
{
    internal const string MacOsMechanism = "macOS sandbox-exec";
    internal const string LinuxBwrapMechanism = "Linux bubblewrap";
    internal const string LinuxLandlockMechanism = "Linux Landlock (setpriv)";

    internal const string WindowsNotice =
        "notice: the bash guard does not support Windows; this command runs unconfined";

    private readonly GuardTarget _target;
    private readonly GuardProbe _probe;
    private GuardCommand? _command;
    private string? _workspace;

    internal BashGuard()
        : this(GuardTarget.Resolve(), GuardProbe.Current) { }

    internal BashGuard(GuardTarget target, GuardProbe probe)
    {
        _target = target;
        _probe = probe;
    }

    /// <summary>
    /// How the given shell is wrapped on this machine, resolved once per guard and reused (the
    /// workspace is fixed per tool instance, and a relative denial target is anchored to it).
    /// A shell that does not exist is never wrapped: the missing shell must surface through the
    /// tool's own spawn error, not through the mechanism failing to exec it.
    /// </summary>
    internal GuardCommand Wrap(ResolvedShell shell, string workspace)
    {
        _workspace = workspace;
        if (_command is { } command)
        {
            return command;
        }

        _command =
            _probe.IsWindows ? UnsupportedWindows(shell)
            : !_probe.FileExists(shell.ExecutablePath) ? Unwrapped(shell, "none", notice: null)
            : _probe.IsMacOs ? ResolveMacOs(shell)
            : ResolveLinux(shell, workspace);
        return _command;
    }

    private static GuardCommand UnsupportedWindows(ResolvedShell shell) =>
        new(
            shell.ExecutablePath,
            [.. shell.ArgumentsPrefix],
            "Windows unsupported",
            Wrapped: false,
            WindowsNotice
        );

    private static GuardCommand Unwrapped(ResolvedShell shell, string mechanism, string? notice) =>
        new(shell.ExecutablePath, [.. shell.ArgumentsPrefix], mechanism, Wrapped: false, notice);
}
