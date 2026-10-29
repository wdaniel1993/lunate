using System.Globalization;
using System.Text;
using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>Per-platform mechanism resolution for <see cref="BashGuard"/>: which wrapper to use,
/// whether it actually starts, and the profile or rule list that denies the targets.</summary>
internal sealed partial class BashGuard
{
    private const string SandboxExecutable = "/usr/bin/sandbox-exec";
    private const string AppArmorSysctl = "/proc/sys/kernel/apparmor_restrict_unprivileged_userns";
    private const int LandlockRuleCap = 64;

    private static readonly IReadOnlyList<string> TempRoots = ["/tmp", "/var/tmp"];

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

    private static string Escape(string path) => path.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // Linux ------------------------------------------------------------------

    private GuardCommand ResolveLinux(ResolvedShell shell, string workspace)
    {
        var reasons = new List<string>();
        string? bwrap = _probe.FindOnPath("bwrap");
        if (bwrap is { } found)
        {
            (int probeExit, string probeOutput) = _probe.Run(
                found,
                [.. BwrapBase(), "/bin/true"]
            );
            if (probeExit == 0)
            {
                IReadOnlyList<string> prefix =
                [
                    .. BwrapBase(),
                    shell.ExecutablePath,
                    .. shell.ArgumentsPrefix,
                ];
                return new GuardCommand(found, prefix, LinuxBwrapMechanism, Wrapped: true, null);
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
            IReadOnlyList<string> prefix =
            [
                .. rules,
                shell.ExecutablePath,
                .. shell.ArgumentsPrefix,
            ];
            return new GuardCommand(setpriv, prefix, LinuxLandlockMechanism, Wrapped: true, null);
        }

        reasons.Add($"setpriv's Landlock probe failed ({landlockOutput})");
        return Unwrapped(shell, "unavailable", Notice(reasons));
    }

    /// <summary>
    /// The guard argument list: the root stays as writable as it is outside, and only the denied
    /// targets — the state root, the install prefix, and <c>auth.json</c> as a read-only overlay —
    /// are bound read-only on top. Ordering matters: the specific binds mount over the writable
    /// root bind, which is the same stacking the rename in bwrap's own <c>--bind</c> semantics
    /// allows. <c>/dev</c> is re-bound so device access behaves outside the mount namespace, and
    /// the sandbox dies with the parent so an orphaned child cannot outlive the tool call.
    /// </summary>
    private List<string> BwrapBase()
    {
        List<string> arguments = ["--bind", "/", "/"];
        arguments.AddRange(TargetBind("--ro-bind", _target.StateRoot));
        arguments.AddRange(TargetBind("--ro-bind", _target.Prefix));
        if (_probe.FileExists(_target.AuthPath))
        {
            // The read-deny: bind an empty file over auth.json, so reads see `/dev/null`, and
            // writes to it land there too.
            arguments.AddRange(["--ro-bind", "/dev/null", _target.AuthPath]);
        }

        arguments.AddRange(["--dev-bind", "/dev", "/dev", "--die-with-parent"]);
        return arguments;

        // A missing source is a bwrap failure, not a protection; skip rather than break the
        // mechanism, and let the write land — the next resolution picks the directory up so the
        // following spawn denies writes under it.
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
}
