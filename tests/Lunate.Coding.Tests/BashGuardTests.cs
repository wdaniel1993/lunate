using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

/// <summary>
/// The guard's own decisions, driven through a fake probe so every platform branch is
/// testable everywhere: the macOS profile, the bwrap line, the setpriv/Landlock fallback,
/// the Windows notice and the refusal that names the guard.
/// </summary>
public sealed class BashGuardTests
{
    private static readonly ResolvedShell Shell = new(ShellKind.Bash, "/bin/bash", "bash", ["-c"]);

    [Fact]
    public void The_macos_plan_wraps_the_shell_in_sandbox_exec_and_denies_the_targets()
    {
        using var temp = new TempDirectory();
        string state = Path.Combine(temp.Root, "state");
        string prefix = Path.Combine(temp.Root, "prefix");
        var target = new GuardTarget(state, Path.Combine(state, "auth.json"), prefix, temp.Root);
        var guard = new BashGuard(target, Fake.Unix(macOs: true, sandboxExec: true));

        var launch = guard.Wrap(Shell, Path.Combine(temp.Root, "ws"));

        Assert.Equal("macOS sandbox-exec", launch.Mechanism);
        Assert.True(launch.Wrapped);
        Assert.Equal("/usr/bin/sandbox-exec", launch.FileName);
        Assert.Equal(["-p", ProfileArg(launch), "/bin/bash", "-c"], launch.ArgumentsPrefix);
        Assert.Contains("(allow default)", ProfileArg(launch));
        string canonicalState = PathCanonical.Canonicalize(state);
        string canonicalPrefix = PathCanonical.Canonicalize(prefix);
        string canonicalAuth = PathCanonical.Canonicalize(Path.Combine(state, "auth.json"));
        Assert.Contains($@"(deny file-write* (subpath ""{canonicalState}""))", ProfileArg(launch));
        Assert.Contains($@"(deny file-write* (subpath ""{canonicalPrefix}""))", ProfileArg(launch));
        Assert.Contains(
            $@"(deny file-read* (literal ""{canonicalAuth}""))",
            ProfileArg(launch)
        );
        Assert.Null(launch.Notice);
    }

    [Fact]
    public void The_bwrap_plan_binds_the_root_read_only_and_the_workspace_writable()
    {
        using var temp = new TempDirectory();
        string state = Path.Combine(temp.Root, "state");
        string prefix = Path.Combine(temp.Root, "prefix");
        string auth = Path.Combine(state, "auth.json");
        string ws = Path.Combine(temp.Root, "ws");
        var target = new GuardTarget(state, auth, prefix, temp.Root);
        var guard = new BashGuard(target, Fake.Unix(macOs: false, bwrap: "/usr/bin/bwrap", authExists: true));

        var launch = guard.Wrap(Shell, ws);

        Assert.Equal("Linux bubblewrap", launch.Mechanism);
        Assert.True(launch.Wrapped);
        Assert.Equal("/usr/bin/bwrap", launch.FileName);
        Assert.Null(launch.Notice);
        var args = string.Join(' ', launch.ArgumentsPrefix);
        Assert.Contains("--ro-bind / /", args);
        Assert.Contains($"--bind {ws} {ws}", args);
        Assert.Contains("--tmpfs /tmp", args);
        Assert.Contains("--dev-bind /dev /dev", args);
        Assert.Contains($"--ro-bind {state} {state}", args);
        Assert.Contains($"--ro-bind {prefix} {prefix}", args);
        Assert.Contains($"--ro-bind /dev/null {auth}", args);
        Assert.Contains("--die-with-parent", args);
        Assert.EndsWith("/bin/bash -c", args);
    }

    [Fact]
    public void Bwrap_that_cannot_start_is_detected_and_reports_the_apparmor_restriction()
    {
        using var temp = new TempDirectory();
        var target = Target(temp.Root);
        var probe = Fake.Unix(
            macOs: false,
            bwrap: "/usr/bin/bwrap",
            bwrapOutput: "bwrap: Creating new namespace failed: Operation not permitted",
            apparmorRestrictsUserns: true
        );
        var guard = new BashGuard(target, probe);

        var launch = guard.Wrap(Shell, temp.Root);

        Assert.False(launch.Wrapped);
        Assert.Equal("/bin/bash", launch.FileName);
        Assert.Contains("apparmor_restrict_unprivileged_userns", launch.Notice ?? "");
        Assert.Contains("Creating new namespace failed", launch.Notice ?? "");
        Assert.Contains("runs unconfined", launch.Notice ?? "");
    }

    [Fact]
    public void A_bwrap_failure_without_the_apparmor_restriction_reports_the_bwrap_error()
    {
        using var temp = new TempDirectory();
        var probe = Fake.Unix(
            macOs: false,
            bwrap: "/usr/bin/bwrap",
            bwrapOutput: "bwrap: Creating new namespace failed: Operation not permitted"
        );
        var guard = new BashGuard(Target(temp.Root), probe);

        var launch = guard.Wrap(Shell, temp.Root);

        Assert.False(launch.Wrapped);
        Assert.Contains("Creating new namespace failed", launch.Notice ?? "");
        Assert.DoesNotContain("apparmor_restrict_unprivileged_userns", launch.Notice ?? "");
        Assert.Contains("runs unconfined", launch.Notice ?? "");
    }

    [Fact]
    public void Linux_without_bwrap_or_setpriv_says_the_guard_is_unavailable()
    {
        using var temp = new TempDirectory();
        var guard = new BashGuard(Target(temp.Root), Fake.Unix(macOs: false, bwrap: null, setpriv: null));

        var launch = guard.Wrap(Shell, temp.Root);

        Assert.False(launch.Wrapped);
        Assert.Equal("/bin/bash", launch.FileName);
        Assert.Equal(["-c"], launch.ArgumentsPrefix);
        Assert.Contains("bubblewrap", launch.Notice ?? "");
        Assert.Contains("setpriv", launch.Notice ?? "");
        Assert.Contains("runs unconfined", launch.Notice ?? "");
    }

    [Fact]
    public void The_landlock_fallback_allowlists_the_workspace_but_never_the_state_root()
    {
        using var temp = new TempDirectory();
        string profile = Path.Combine(temp.Root, "profile");
        string state = Path.Combine(profile, "state");
        string prefix = Path.Combine(profile, ".local", "bin");
        string ws = Path.Combine(temp.Root, "ws");
        var target = new GuardTarget(state, Path.Combine(state, "auth.json"), prefix, profile);
        var probe = Fake.Unix(
            macOs: false,
            bwrap: null,
            setpriv: "/usr/bin/setpriv",
            profileChildren:
            [
                Path.Combine(profile, "projects"),
                Path.Combine(profile, "state"),
                Path.Combine(profile, ".local"),
            ],
            localChildren:
            [Path.Combine(profile, ".local", "bin"), Path.Combine(profile, ".local", "share")]
        );
        var guard = new BashGuard(target, probe);

        var launch = guard.Wrap(Shell, ws);

        Assert.Equal("Linux Landlock (setpriv)", launch.Mechanism);
        Assert.True(launch.Wrapped);
        Assert.Equal("/usr/bin/setpriv", launch.FileName);
        var args = string.Join(' ', launch.ArgumentsPrefix);
        Assert.Contains("--landlock-access fs", args);
        Assert.Contains($"path-beneath::{ws}", args);
        Assert.Contains("path-beneath::/tmp", args);
        Assert.Contains($"path-beneath::{Path.Combine(profile, "projects")}", args);
        Assert.Contains($"path-beneath::{Path.Combine(profile, ".local", "share")}", args);
        Assert.DoesNotContain($"path-beneath::{state}", args);
        Assert.DoesNotContain($"path-beneath::{prefix}", args);
        Assert.DoesNotContain("--ro-bind", args);
        Assert.EndsWith("/bin/bash -c", args);
    }

    [Fact]
    public void Setpriv_that_fails_its_landlock_probe_falls_to_an_unavailable_notice()
    {
        using var temp = new TempDirectory();
        var probe = Fake.Unix(
            macOs: false,
            bwrap: null,
            setpriv: "/usr/bin/setpriv",
            setprivOutput: "setpriv: invalid Landlock access: fs"
        );
        var guard = new BashGuard(Target(temp.Root), probe);

        var launch = guard.Wrap(Shell, temp.Root);

        Assert.False(launch.Wrapped);
        Assert.Contains("setpriv", launch.Notice ?? "");
        Assert.Contains("invalid Landlock access", launch.Notice ?? "");
        Assert.Contains("runs unconfined", launch.Notice ?? "");
    }

    [Fact]
    public void On_windows_the_guard_names_itself_unsupported()
    {
        using var temp = new TempDirectory();
        var guard = new BashGuard(Target(temp.Root), Fake.Unix(macOs: false, isWindows: true));

        var launch = guard.Wrap(Shell, temp.Root);

        Assert.False(launch.Wrapped);
        Assert.Equal("/bin/bash", launch.FileName);
        Assert.Equal(["-c"], launch.ArgumentsPrefix);
        Assert.Contains("Windows", launch.Notice ?? "");
        Assert.Contains("unconfined", launch.Notice ?? "");
    }

    [Theory]
    [InlineData("macOS sandbox-exec")]
    [InlineData("Linux bubblewrap")]
    public void A_redirect_into_the_state_root_is_refused_before_the_run_and_names_the_guard(
        string mechanism
    )
    {
        using var temp = new TempDirectory();
        string state = Path.Combine(temp.Root, "state");
        var target = new GuardTarget(state, Path.Combine(state, "auth.json"), Path.Combine(temp.Root, "prefix"), temp.Root);
        var probe = mechanism switch
        {
            "macOS sandbox-exec" => Fake.Unix(macOs: true, sandboxExec: true),
            _ => Fake.Unix(macOs: false, bwrap: "/usr/bin/bwrap"),
        };
        var guard = new BashGuard(target, probe);
        var launch = guard.Wrap(Shell, Path.Combine(temp.Root, "ws"));

        var refusal = guard.Refusal($"echo pwn > {state}/trust.json", launch);

        Assert.NotNull(refusal);
        Assert.Contains("refused by the bash guard", refusal);
        Assert.Contains(mechanism, refusal);
        Assert.Contains(state, refusal);
    }

    [Theory]
    [InlineData("cat /tmp/profile/state/auth.json")]
    [InlineData("ls /tmp/profile/state")]
    [InlineData("echo pwn > /tmp/profile/workspace/out.txt")]
    [InlineData("echo \"not a redirect > /tmp/profile/state/x\"")]
    [InlineData("cp /tmp/profile/state/auth.json copy.json")]
    public void Commands_that_do_not_write_a_denied_target_are_not_refused(string commandText)
    {
        using var temp = new TempDirectory();
        var guard = new BashGuard(Target(temp.Root), Fake.Unix(macOs: true, sandboxExec: true));
        var launch = guard.Wrap(Shell, Path.Combine(temp.Root, "ws"));

        Assert.Null(guard.Refusal(commandText, launch));
    }

    [Fact]
    public void A_redirect_through_the_home_spelling_of_the_state_root_is_refused()
    {
        var guard = new BashGuard(
            new GuardTarget(
                "/home/dev/.lunate",
                "/home/dev/.lunate/auth.json",
                "/home/dev/.local/bin",
                "/home/dev"
            ),
            Fake.Unix(macOs: true, sandboxExec: true)
        );
        var launch = guard.Wrap(Shell, "/home/dev/ws");

        Assert.NotNull(guard.Refusal("echo pwn >> ~/.lunate/trust.json", launch));
        Assert.NotNull(guard.Refusal("echo pwn > $LUNATE_HOME/trust.json", launch));
    }

    [Theory]
    [InlineData("macOS sandbox-exec", "/bin/bash: /tmp/state/trust.json: Operation not permitted")]
    [InlineData(
        "Linux bubblewrap",
        "/bin/bash: line 1: /tmp/state/trust.json: Read-only file system"
    )]
    public void A_filesystem_denial_against_a_denied_target_gets_a_hint_naming_the_guard(
        string mechanism,
        string stderr
    )
    {
        using var temp = new TempDirectory();
        var target = Target(temp.Root);
        var probe = mechanism switch
        {
            "macOS sandbox-exec" => Fake.Unix(macOs: true, sandboxExec: true),
            _ => Fake.Unix(macOs: false, bwrap: "/usr/bin/bwrap"),
        };
        var guard = new BashGuard(target, probe);
        var launch = guard.Wrap(Shell, Path.Combine(temp.Root, "ws"));

        var hint = guard.Hint($"mv {target.StateRoot}/a {target.StateRoot}/b", stderr, launch);

        Assert.NotNull(hint);
        Assert.Contains("bash guard", hint);
        Assert.Contains(mechanism, hint);
    }

    [Theory]
    [InlineData("hello", "Permission denied")]
    [InlineData("rm /tmpstate/x", "/bin/bash: /tmpstate/x: Operation not permitted")]
    public void A_failure_without_a_denied_target_does_not_get_a_guard_hint(
        string commandText,
        string stderr
    )
    {
        using var temp = new TempDirectory();
        var guard = new BashGuard(Target(temp.Root), Fake.Unix(macOs: true, sandboxExec: true));
        var launch = guard.Wrap(Shell, Path.Combine(temp.Root, "ws"));

        Assert.Null(guard.Hint(commandText, stderr, launch));
    }

    private static GuardTarget Target(string root) =>
        new(Path.Combine(root, "state"), Path.Combine(root, "state", "auth.json"), Path.Combine(root, "prefix"), root);

    private static string ProfileArg(GuardCommand launch) => launch.ArgumentsPrefix[1];

    private static class Fake
    {
        internal static GuardProbe Unix(
            bool macOs,
            bool sandboxExec = false,
            string? bwrap = null,
            string? setpriv = null,
            string bwrapOutput = "",
            string setprivOutput = "",
            bool? apparmorRestrictsUserns = null,
            IReadOnlyList<string>? profileChildren = null,
            IReadOnlyList<string>? localChildren = null,
            bool authExists = false,
            bool isWindows = false
        )
        {
            string? byName(string name) =>
                (name, sandboxExec, bwrap, setpriv) switch
                {
                    ("sandbox-exec", true, _, _) => "/usr/bin/sandbox-exec",
                    ("bwrap", _, not null, _) => "/usr/bin/bwrap",
                    ("setpriv", _, _, not null) => "/usr/bin/setpriv",
                    _ => null,
                };

            return new GuardProbe(
                IsWindows: isWindows,
                IsMacOs: macOs,
                IsLinux: !macOs && !isWindows,
                FindOnPath: byName,
                FileExists: path =>
                    sandboxExec && path == "/usr/bin/sandbox-exec"
                    || authExists && path.EndsWith("auth.json", StringComparison.Ordinal),
                DirectoryExists: _ => true,
                GetEnvironmentVariable: _ => null,
                ReadTextFile: _ =>
                    apparmorRestrictsUserns is not { } value ? null : value ? "1" : "0",
                ListDirectories: path =>
                    path.EndsWith(".local", StringComparison.Ordinal)
                        ? localChildren ?? []
                        : path is "/tmp" or "/var/tmp"
                            ? []
                            : profileChildren ?? [],
                Run: (fileName, _) =>
                    fileName.Contains("bwrap", StringComparison.Ordinal)
                        ? (bwrapOutput.Length == 0 ? 0 : 1, bwrapOutput)
                        : fileName.Contains("setpriv", StringComparison.Ordinal)
                            ? (setprivOutput.Length == 0 ? 0 : 1, setprivOutput)
                            : (0, "")
            );
        }
    }
}
