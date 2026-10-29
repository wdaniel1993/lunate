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

    /// <summary>
    /// The rule budget. A typical Linux system lands in the low hundreds of allow rules (one per
    /// top-level directory, plus the branches past a denied target), about 30 KB of argv per
    /// spawn; past the cap the mechanism reports unavailable instead of ballooning every spawn
    /// or silently tightening the profile.
    /// </summary>
    private const int LandlockRuleCap = 256;

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
            (int probeExit, string probeOutput) = _probe.Run(found, [.. BwrapBase(), "/bin/true"]);
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
    /// The Landlock fallback: Landlock has no deny rules, so the profile is written as allow rules
    /// with rights per rule. The walk starts at the filesystem root: every directory that holds no
    /// denied target gets one full rule, and its subtree keeps working normally as a result; the
    /// branches leading down to the targets stay minimal (listing plus the files directly in
    /// them), the state root and the install prefix are granted read-only (prefix also
    /// executable, so its binaries still run). The rights are spelled in setpriv's rule syntax; an
    /// empty access field (<c>path-beneath::path</c>) means every handled access.
    /// <c>auth.json</c> gets no rule at all: nothing beneath the state root grants
    /// <c>read-file</c> on it, so its reads are denied along with its writes.
    /// </summary>
    private List<string>? LandlockBase(string workspace, out int ruleCount)
    {
        var rules = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (string root in new[] { "/", workspace }.Distinct(StringComparer.Ordinal))
        {
            if (visited.Add(root))
            {
                Walk(root, self: root != "/", rules, visited);
            }
        }

        List<string> ordered = rules.Distinct(StringComparer.Ordinal).ToList();
        ruleCount = ordered.Count;
        if (ruleCount > LandlockRuleCap)
        {
            return null;
        }

        var arguments = new List<string> { "--landlock-access", "fs" };
        foreach (string rule in ordered)
        {
            arguments.Add("--landlock-rule");
            arguments.Add(rule);
        }

        return arguments;
    }

    /// <summary>Grants the rights beneath <paramref name="directory"/>. <paramref name="self"/>
    /// is true for a walk root that contains no denied target: the whole subtree stays normal. On
    /// the path to a denied target, and inside the targets, grants narrow to reads (plus execute
    /// in the prefix): Landlock is allow-only, so the deny comes from what no rule grants.</summary>
    private void Walk(string directory, bool self, List<string> rules, HashSet<string> visited)
    {
        if (DirectoryEquals(directory, _target.StateRoot))
        {
            // Read-only subtree: listable here, readable beneath each child, never writable —
            // and the files directly in it stay readable except auth.json, which gets nothing.
            rules.Add($"path-beneath:read-dir:{directory}");
            foreach (string child in _probe.ListDirectories(directory))
            {
                if (!visited.Add(child))
                {
                    continue;
                }

                rules.Add($"path-beneath:read-file,read-dir:{child}");
            }

            GrantFiles(directory, "read-file", rules, visited);
            return;
        }

        if (DirectoryEquals(directory, _target.Prefix))
        {
            // The install dir: running lunate must keep working, writing into it must not.
            rules.Add($"path-beneath:execute,read-file,read-dir:{directory}");
            GrantFiles(directory, "execute,read-file", rules, visited);
            return;
        }

        bool onPath = _target.ContainsDenied(directory);
        if (onPath)
        {
            rules.Add($"path-beneath:read-dir:{directory}");
            GrantFiles(directory, "read-file", rules, visited);
        }
        else if (self)
        {
            rules.Add($"path-beneath::{directory}");
            return;
        }

        foreach (string child in _probe.ListDirectories(directory))
        {
            if (!visited.Add(child))
            {
                continue;
            }

            if (_target.ContainsDenied(child))
            {
                Walk(child, self: false, rules, visited);
            }
            else
            {
                rules.Add($"path-beneath::{child}");
            }
        }
    }

    /// <summary>Reads for the files directly inside a directory whose own rule does not cover
    /// them (rule rights apply beneath a directory rule, so a file rule is needed per file).
    /// <c>auth.json</c> is skipped everywhere it can appear.</summary>
    private void GrantFiles(
        string directory,
        string rights,
        List<string> rules,
        HashSet<string> visited
    )
    {
        foreach (string file in _probe.ListFiles(directory))
        {
            if (_target.IsDeniedFile(file) || !visited.Add(file))
            {
                continue;
            }

            rules.Add($"path-beneath:{rights}:{file}");
        }
    }

    private static bool DirectoryEquals(string path, string root) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(PathCanonical.Canonicalize(path)),
            Path.TrimEndingDirectorySeparator(PathCanonical.Canonicalize(root)),
            StringComparison.Ordinal
        );

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
