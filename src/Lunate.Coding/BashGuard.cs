using System.Globalization;
using System.Text;
using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>
/// The targets the bash guard protects: the state root that holds trust records, sessions and
/// settings, the install prefix that holds the <c>lunate</c> binary, and the credential file that
/// is additionally read-denied. <see cref="ProfileBase"/> is the directory the shell's <c>~</c>
/// spellings and the Landlock walk expand against.
/// </summary>
internal sealed record GuardTarget(string StateRoot, string AuthPath, string Prefix, string ProfileBase)
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
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(binary))
            ?? LunateHome.Profile;
        return string.IsNullOrWhiteSpace(directory) ? LunateHome.Profile : directory;
    }

    /// <summary>Whether <paramref name="path"/> is, or lies beneath, a denied target.</summary>
    internal bool IsDenied(string path) =>
        LunateHome.IsInside(path, StateRoot) || LunateHome.IsInside(path, Prefix);

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
internal sealed class BashGuard
{
    internal const string MacOsMechanism = "macOS sandbox-exec";
    internal const string LinuxBwrapMechanism = "Linux bubblewrap";
    internal const string LinuxLandlockMechanism = "Linux Landlock (setpriv)";

    internal const string WindowsNotice =
        "notice: the bash guard does not support Windows; this command runs unconfined";

    private const string SandboxExecutable = "/usr/bin/sandbox-exec";
    private const string AppArmorSysctl = "/proc/sys/kernel/apparmor_restrict_unprivileged_userns";
    private const int LandlockRuleCap = 64;

    private readonly GuardTarget _target;
    private readonly GuardProbe _probe;
    private GuardCommand? _command;

    internal BashGuard()
        : this(GuardTarget.Resolve(), GuardProbe.Current) { }

    internal BashGuard(GuardTarget target, GuardProbe probe)
    {
        _target = target;
        _probe = probe;
    }

    /// <summary>
    /// How the given shell is wrapped on this machine, resolved once per guard and reused (the
    /// workspace is fixed per tool instance).
    /// </summary>
    internal GuardCommand Wrap(ResolvedShell shell, string workspace)
    {
        if (_command is { } command)
        {
            return command;
        }

        _command = _probe.IsWindows
            ? UnsupportedWindows(shell)
            : _probe.IsMacOs
                ? ResolveMacOs(shell)
                : ResolveLinux(shell, workspace);
        return _command;
    }

    /// <summary>
    /// The pre-run refusal for a command that would write a denied target: refused without a spawn,
    /// naming the guard, because the mechanism would refuse it a moment later in OS terms.
    /// </summary>
    internal string? Refusal(string command, GuardCommand launch)
    {
        if (!launch.Wrapped)
        {
            return null;
        }

        return WriteTargetIn(command) switch
        {
            string denied => string.Create(
                CultureInfo.InvariantCulture,
                $"refused by the bash guard ({launch.Mechanism}): the command would write under "
                    + $"{denied}, and the guard denies writes under {_target.StateRoot} and "
                    + $"{_target.Prefix}. Write inside the workspace instead."
            ),
            null => null,
        };
    }

    /// <summary>
    /// The guard's credit for a failure the mechanism most likely caused: the command touched a
    /// denied target and its stderr shows a filesystem denial. Best-effort, so it says "may".
    /// </summary>
    internal string? Hint(string command, string stderr, GuardCommand launch)
    {
        if (!launch.Wrapped)
        {
            return null;
        }

        bool denied = stderr.Contains("Operation not permitted", StringComparison.Ordinal)
            || stderr.Contains("Permission denied", StringComparison.Ordinal)
            || stderr.Contains("Read-only file system", StringComparison.Ordinal);
        bool touched = command.Contains(_target.StateRoot, StringComparison.OrdinalIgnoreCase)
            || command.Contains(_target.Prefix, StringComparison.OrdinalIgnoreCase)
            || command.Contains(_target.AuthPath, StringComparison.OrdinalIgnoreCase);
        return denied && touched
            ? $"maybe refused by the bash guard ({launch.Mechanism}): the command touched a denied "
                + "target and failed with a filesystem denial (writes under the state root and the "
                + "install prefix are denied)."
            : null;
    }

    // macOS ------------------------------------------------------------------

    private const string UnknownMacNotice =
        "the bash guard could not start: no /usr/bin/sandbox-exec; this command runs unconfined";

    private GuardCommand ResolveMacOs(ResolvedShell shell)
    {
        if (!_probe.FileExists(SandboxExecutable))
        {
            return Unwrapped(shell, "none", UnknownMacNotice);
        }

        IReadOnlyList<string> prefix =
        [
            "-p",
            MacProfile(),
            shell.ExecutablePath,
            .. shell.ArgumentsPrefix,
        ];
        return new GuardCommand(SandboxExecutable, prefix, MacOsMechanism, Wrapped: true, null);
    }

    /// <summary>
    /// Profile A from the spike: allow everything, deny the two targets plus <c>auth.json</c>
    /// reads. The paths are written canonically — Seatbelt matches resolved paths, and a
    /// symlinked spelling of the same directory (e.g. <c>/var/…</c> for <c>/private/var/…</c> on
    /// macOS) would otherwise be silently un-denied.
    /// </summary>
    private string MacProfile() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"""
            (version 1)
            (allow default)
            (deny file-write* (subpath "{Escape(PathCanonical.Canonicalize(_target.StateRoot))}"))
            (deny file-write* (subpath "{Escape(PathCanonical.Canonicalize(_target.Prefix))}"))
            (deny file-read* (literal "{Escape(PathCanonical.Canonicalize(_target.AuthPath))}"))
            """
        );

    private static string Escape(string path) => path
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"");

    // Linux ------------------------------------------------------------------

    private static readonly IReadOnlyList<string> TempRoots = ["/tmp", "/var/tmp"];

    private GuardCommand ResolveLinux(ResolvedShell shell, string workspace)
    {
        var reasons = new List<string>();
        string? bwrap = _probe.FindOnPath("bwrap");
        if (bwrap is { } found)
        {
            (int probeExit, string probeOutput) = _probe.Run(
                found,
                [.. BwrapBase(workspace), "/bin/true"]
            );
            if (probeExit == 0)
            {
                IReadOnlyList<string> prefix =
                [
                    .. BwrapBase(workspace),
                    shell.ExecutablePath,
                    .. shell.ArgumentsPrefix,
                ];
                return new GuardCommand(
                    found,
                    prefix,
                    LinuxBwrapMechanism,
                    Wrapped: true,
                    null
                );
            }

            reasons.Add($"bubblewrap could not start ({probeOutput})");
        }
        else
        {
            reasons.Add("bubblewrap was not found");
        }

        string? setpriv = _probe.FindOnPath("setpriv");
        if (setpriv is null)
        {
            reasons.Add("setpriv (Landlock) was not found");
            return Unwrapped(shell, "unavailable", Notice(reasons));
        }

        List<string>? rules = LandlockBase(workspace, out int ruleCount);
        if (rules is null)
        {
            reasons.Add(
                $"the Landlock fallback would need {ruleCount} allow rules and stopped itself"
            );
            return Unwrapped(shell, "unavailable", Notice(reasons));
        }

        (int landlockExit, string landlockOutput) = _probe.Run(setpriv, [.. rules, "/bin/true"]);
        if (landlockExit == 0)
        {
            IReadOnlyList<string> prefix = [.. rules, shell.ExecutablePath, .. shell.ArgumentsPrefix];
            return new GuardCommand(
                setpriv,
                prefix,
                LinuxLandlockMechanism,
                Wrapped: true,
                null
            );
        }

        reasons.Add($"setpriv's Landlock probe failed ({landlockOutput})");
        return Unwrapped(shell, "unavailable", Notice(reasons));
    }

    private List<string> BwrapBase(string workspace)
    {
        var arguments = new List<string>
        {
            "--ro-bind",
            "/",
            "/",
            "--bind",
            workspace,
            workspace,
            "--tmpfs",
            "/tmp",
            "--dev-bind",
            "/dev",
            "/dev",
            "--die-with-parent",
        };
        arguments.AddRange(TargetBind("--ro-bind", _target.StateRoot));
        arguments.AddRange(TargetBind("--ro-bind", _target.Prefix));
        if (_probe.FileExists(_target.AuthPath))
        {
            // The read-deny: bind an empty file over auth.json, so reads see `/dev/null`, and
            // writes to it land there too.
            arguments.AddRange(["--ro-bind", "/dev/null", _target.AuthPath]);
        }

        return arguments;

        // A missing source is a bwrap failure, not a protection; the ro-bind of / already makes a
        // missing state root un-writable, so skip rather than break the mechanism.
        IEnumerable<string> TargetBind(string flag, string path) =>
            _probe.DirectoryExists(path) ? [flag, path, path] : [];
    }

    /// <summary>
    /// The Landlock fallback: Landlock has no deny rules, so "everything except the targets" is
    /// written as allow rules — every sibling of every denied target, all the way down. The
    /// workspace, <c>/tmp</c> bases and <c>/dev/null</c> are allowed outright.
    /// </summary>
    private List<string>? LandlockBase(string workspace, out int ruleCount)
    {
        var allowed = new List<string> { workspace, "/dev/null" };
        foreach (string root in WalkRoots())
        {
            if (!_target.ContainsDenied(root))
            {
                allowed.Add(root);
                continue;
            }

            allowed.AddRange(Walk(root));
        }

        ruleCount = allowed.Count;
        if (allowed.Count > LandlockRuleCap)
        {
            return null;
        }

        var arguments = new List<string> { "--landlock-access", "fs" };
        foreach (string path in allowed.Distinct(StringComparer.Ordinal))
        {
            arguments.Add("--landlock-rule");
            arguments.Add($"path-beneath::{path}");
        }

        return arguments;
    }

    private IEnumerable<string> WalkRoots()
    {
        string? runtimeDirectory = _probe.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        IEnumerable<string> roots = TempRoots;
        if (!string.IsNullOrWhiteSpace(_target.ProfileBase))
        {
            roots = roots.Append(_target.ProfileBase);
        }

        if (!string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            roots = roots.Append(runtimeDirectory);
        }

        return roots.Distinct(StringComparer.Ordinal);
    }

    private IReadOnlyList<string> Walk(string root)
    {
        List<string> allowed = [];
        foreach (string child in _probe.ListDirectories(root))
        {
            if (_target.IsDenied(child))
            {
                continue;
            }

            if (_target.ContainsDenied(child))
            {
                allowed.AddRange(Walk(child));
            }
            else
            {
                allowed.Add(child);
            }
        }

        return allowed;
    }

    private string Notice(IReadOnlyList<string> reasons)
    {
        var text = new StringBuilder("the bash guard is unavailable: ");
        text.Append(string.Join("; ", reasons));
        if (_probe.ReadTextFile(AppArmorSysctl) == "1")
        {
            text.Append(
                "; user namespaces are restricted (apparmor_restrict_unprivileged_userns=1)"
            );
        }

        text.Append("; this command runs unconfined");
        return text.ToString();
    }

    private static GuardCommand UnsupportedWindows(ResolvedShell shell) =>
        new(shell.ExecutablePath, [.. shell.ArgumentsPrefix], "Windows unsupported", Wrapped: false, WindowsNotice);

    private static GuardCommand Unwrapped(ResolvedShell shell, string mechanism, string? notice) =>
        new(shell.ExecutablePath, [.. shell.ArgumentsPrefix], mechanism, Wrapped: false, notice);

    // The pre-run scan -------------------------------------------------------

    /// <summary>
    /// The path a command's redirection would write into, when it is a denied target. Conservative
    /// by design: a skipped or self-spelling token (<c>$HOME</c>, bare <c>|</c> notation) is left to
    /// the mechanism itself, and obscured quoting is covered by the same backstop.
    /// </summary>
    private string? WriteTargetIn(string command)
    {
        var singleQuote = false;
        var doubleQuote = false;
        for (var index = 0; index < command.Length; index++)
        {
            char current = command[index];
            bool inQuote = singleQuote || doubleQuote;
            if (inQuote && current == '\\' && index + 1 < command.Length)
            {
                index++;
                continue;
            }

            if (!doubleQuote && current == '\'')
            {
                singleQuote = !singleQuote;
                continue;
            }

            if (!singleQuote && current == '"')
            {
                doubleQuote = !doubleQuote;
                continue;
            }

            if (inQuote || current != '>')
            {
                continue;
            }

            while (
                index + 1 < command.Length
                && (command[index + 1] == '>' || command[index + 1] == '|' || command[index + 1] == '&')
            )
            {
                index++;
            }

            while (
                index + 1 < command.Length
                && (command[index + 1] == ' ' || command[index + 1] == '\t')
            )
            {
                index++;
            }

            string? token = ReadWord(command, index + 1);
            if (DenyTarget(token) is { } deniedPath)
            {
                return deniedPath;
            }
        }

        return null;
    }

    private static string? ReadWord(string command, int start)
    {
        if (start >= command.Length)
        {
            return null;
        }

        int end = start;
        while (
            end < command.Length
            && command[end] is not (' ' or '\t' or ';' or '|' or '&' or '<' or '\n')
        )
        {
            end++;
        }

        if (end == start)
        {
            return null;
        }

        string token = command[start..end].Trim('\'', '"');
        return token.Length == 0 ? null : token;
    }

    private string? DenyTarget(string? token)
    {
        if (token is null)
        {
            return null;
        }

        string candidate = Expand(token);
        if (!candidate.Contains('/', StringComparison.Ordinal))
        {
            // A relative token points into the shell's working directory (the workspace), which
            // the guard never denies.
            return null;
        }

        try
        {
            if (LunateHome.IsInside(candidate, _target.StateRoot))
            {
                return _target.StateRoot;
            }

            return LunateHome.IsInside(candidate, _target.Prefix) ? _target.Prefix : null;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private string Expand(string token)
    {
        string expanded = token.Contains("$LUNATE_HOME", StringComparison.Ordinal)
            ? token.Replace("$LUNATE_HOME", _target.StateRoot)
            : token;
        if (expanded.StartsWith("~/", StringComparison.Ordinal))
        {
            expanded = Path.Combine(_target.ProfileBase, expanded[2..]);
        }
        else if (expanded.StartsWith("$HOME/", StringComparison.Ordinal))
        {
            expanded = Path.Combine(_target.ProfileBase, expanded[6..]);
        }

        return expanded;
    }
}
