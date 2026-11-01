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
    internal static readonly ResolvedShell Shell = new(ShellKind.Bash, "/bin/bash", "bash", ["-c"]);

    [Fact]
    public void The_mechanism_probe_passes_arguments_to_the_process_it_starts()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // /bin/echo below; the Windows leg covers the guard when it exists at all.
        }

        (int exit, string output) = GuardProbe.Current.Run(
            "/bin/echo",
            ["guard-arg-one", "guard-arg-two"]
        );

        Assert.Equal(0, exit);
        Assert.Contains("guard-arg-one", output);
        Assert.Contains("guard-arg-two", output);
    }

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

        // The profile is read by sandbox-exec, which knows one separator: the plan must render POSIX
        // paths even when the test runs on a host that spells them with backslashes.
        string canonicalState = Posix(PathCanonical.Canonicalize(state));
        string canonicalPrefix = Posix(PathCanonical.Canonicalize(prefix));
        string canonicalAuth = Posix(PathCanonical.Canonicalize(Path.Combine(state, "auth.json")));
        Assert.Contains($@"(deny file-write* (subpath ""{canonicalState}""))", ProfileArg(launch));
        Assert.Contains($@"(deny file-write* (subpath ""{canonicalPrefix}""))", ProfileArg(launch));
        Assert.Contains($@"(deny file-read* (literal ""{canonicalAuth}""))", ProfileArg(launch));

        // A backslash anywhere in the scheme means a host spelling leaked into a macOS profile; this
        // assertion holds on every runner, including the one whose paths are the reason it exists.
        Assert.DoesNotContain("\\", ProfileArg(launch));
        Assert.Null(launch.Notice);
    }

    [Fact]
    public void The_bwrap_plan_keeps_the_root_writable_and_denies_only_the_targets()
    {
        using var temp = new TempDirectory();
        string state = Path.Combine(temp.Root, "state");
        string prefix = Path.Combine(temp.Root, "prefix");
        string auth = Path.Combine(state, "auth.json");
        string ws = Path.Combine(temp.Root, "ws");
        var target = new GuardTarget(state, auth, prefix, temp.Root);
        var guard = new BashGuard(
            target,
            Fake.Unix(macOs: false, bwrap: "/usr/bin/bwrap", authExists: true)
        );

        var launch = guard.Wrap(Shell, ws);

        Assert.Equal("Linux bubblewrap", launch.Mechanism);
        Assert.True(launch.Wrapped);
        Assert.Equal("/usr/bin/bwrap", launch.FileName);
        Assert.Null(launch.Notice);
        var args = string.Join(' ', launch.ArgumentsPrefix);
        // The guard property: normal writes survive, only the denied targets are read-only...
        Assert.Contains("--bind / /", args);
        Assert.DoesNotContain("--ro-bind / /", args);
        Assert.Contains($"--ro-bind {state} {state}", args);
        Assert.Contains($"--ro-bind {prefix} {prefix}", args);
        Assert.Contains($"--ro-bind /dev/null {auth}", args);
        // ...and normal work is not reshaped: the workspace already sits in the writable root
        // and /tmp keeps whatever a previous command left there.
        Assert.DoesNotContain("--tmpfs /tmp", args);
        Assert.DoesNotContain($"--bind {ws}", args);
        Assert.Contains("--dev-bind /dev /dev", args);
        Assert.Contains("--die-with-parent", args);
        Assert.EndsWith("/bin/bash -c", args);
    }

    [Fact]
    public void A_missing_target_is_masked_with_a_tmpfs_and_never_created_on_the_host()
    {
        using var temp = new TempDirectory();
        string state = Path.Combine(temp.Root, "state");
        string prefix = Path.Combine(temp.Root, "prefix");
        var target = new GuardTarget(state, Path.Combine(state, "auth.json"), prefix, temp.Root);
        GuardProbe probe = Fake.Unix(
            macOs: false,
            bwrap: "/usr/bin/bwrap",
            authExists: false,
            directoryExists: path => path != state && path != prefix
        );
        var guard = new BashGuard(target, probe);

        var launch = guard.Wrap(Shell, Path.Combine(temp.Root, "ws"));

        var args = string.Join(' ', launch.ArgumentsPrefix);
        Assert.Contains($"--tmpfs {state}", args);
        Assert.Contains($"--tmpfs {prefix}", args);
        Assert.DoesNotContain($"--ro-bind {state} {state}", args);

        // The guard must not create what it denies: a refusal elsewhere in the session relies on
        // nothing appearing where the store was pointed.
        Assert.False(Directory.Exists(state));
        Assert.False(Directory.Exists(prefix));
    }

    [Fact]
    public void A_missing_state_root_makes_the_landlock_fallback_unavailable()
    {
        using var temp = new TempDirectory();
        string state = Path.Combine(temp.Root, "state");
        var target = new GuardTarget(
            state,
            Path.Combine(state, "auth.json"),
            Path.Combine(temp.Root, "prefix"),
            temp.Root
        );
        GuardProbe probe = Fake.Unix(
            macOs: false,
            setpriv: "/usr/bin/setpriv",
            directoryExists: path => path != state
        );
        var guard = new BashGuard(target, probe);

        var launch = guard.Wrap(Shell, Path.Combine(temp.Root, "ws"));

        Assert.False(launch.Wrapped);
        Assert.Equal("unavailable", launch.Mechanism);
        Assert.Contains("Landlock cannot deny writes under it", launch.Notice ?? "");
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
        var guard = new BashGuard(
            Target(temp.Root),
            Fake.Unix(macOs: false, bwrap: null, setpriv: null)
        );

        var launch = guard.Wrap(Shell, temp.Root);

        Assert.False(launch.Wrapped);
        Assert.Equal("/bin/bash", launch.FileName);
        Assert.Equal(["-c"], launch.ArgumentsPrefix);
        Assert.Contains("bubblewrap", launch.Notice ?? "");
        Assert.Contains("setpriv", launch.Notice ?? "");
        Assert.Contains("runs unconfined", launch.Notice ?? "");
    }

    [Fact]
    public void The_landlock_fallback_never_grants_access_to_auth_json()
    {
        using var temp = new TempDirectory();
        string profile = Path.Combine(temp.Root, "profile");
        string state = Path.Combine(profile, "state");
        string auth = Path.Combine(state, "auth.json");
        string settings = Path.Combine(state, "settings.json");
        string ws = Path.Combine(temp.Root, "ws");
        var target = new GuardTarget(state, auth, Path.Combine(temp.Root, "prefix"), temp.Root);
        var guard = new BashGuard(
            target,
            Fake.Unix(
                macOs: false,
                setpriv: "/usr/bin/setpriv",
                directoryChildren: new Dictionary<string, IReadOnlyList<string>>(
                    StringComparer.Ordinal
                )
                {
                    ["/"] = [temp.Root, "/tmp"],
                    ["/tmp"] = [],
                    [temp.Root] = [profile],
                    [profile] = [Path.Combine(profile, "projects"), Path.Combine(profile, "state")],
                    [state] = [Path.Combine(state, "sessions")],
                },
                fileChildren: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    [state] = [auth, settings],
                }
            )
        );

        var launch = guard.Wrap(Shell, ws);

        Assert.True(launch.Wrapped);
        var args = string.Join(' ', launch.ArgumentsPrefix);
        // The target itself stays listable, and the files beside auth.json stay readable...
        Assert.Contains($"path-beneath:read-dir:{state}", args);
        Assert.Contains($"path-beneath:read-file,read-dir:{Path.Combine(state, "sessions")}", args);
        Assert.Contains($"path-beneath:read-file:{settings}", args);
        // ...but nothing grants anything for auth.json: no rule covers it, so its reads and its
        // writes are both denied.
        Assert.DoesNotContain(auth, args);
    }

    [Fact]
    public void The_landlock_fallback_keeps_normal_access_outside_the_targets_alive()
    {
        // The pre-fix fallback walked only temp/profile roots, so every other directory —
        // /etc below, and files directly in the profile — was dragged into the deny. A guard
        // must leave ordinary access alone.
        using var temp = new TempDirectory();
        string home = Path.Combine(temp.Root, "home");
        string state = Path.Combine(home, "dev", ".lunate");
        string auth = Path.Combine(state, "auth.json");
        string prefix = Path.Combine(home, "dev", ".local", "bin");
        var target = new GuardTarget(state, auth, prefix, home);
        var guard = new BashGuard(
            target,
            Fake.Unix(
                macOs: false,
                setpriv: "/usr/bin/setpriv",
                directoryChildren: new Dictionary<string, IReadOnlyList<string>>(
                    StringComparer.Ordinal
                )
                {
                    ["/"] = ["/etc", "/usr", home],
                    ["/etc"] = [],
                    ["/usr"] = [],
                    [home] = [Path.Combine(home, "dev"), Path.Combine(home, "other")],
                    [Path.Combine(home, "other")] = [],
                    [Path.Combine(home, "dev")] =
                    [
                        Path.Combine(home, "dev", "projects"),
                        Path.Combine(home, "dev", ".local"),
                    ],
                    [Path.Combine(home, "dev", "projects")] = [],
                    [Path.Combine(home, "dev", ".local")] = [prefix],
                    [prefix] = [],
                },
                fileChildren: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    [home] = [Path.Combine(home, ".gitconfig")],
                }
            )
        );

        var launch = guard.Wrap(Shell, Path.Combine(home, "dev", "ws"));

        Assert.True(launch.Wrapped);
        var args = string.Join(' ', launch.ArgumentsPrefix);
        // Ordinary directories, however far from the profile, are granted whole...
        Assert.Contains("path-beneath::/etc", args);
        Assert.Contains("path-beneath::/usr", args);
        Assert.Contains($"path-beneath::{Path.Combine(home, "other")}", args);
        // ...and so are reads of files that sit directly on the path to a target.
        Assert.Contains($"path-beneath:read-file:{Path.Combine(home, ".gitconfig")}", args);
        Assert.DoesNotContain($"path-beneath::{state}", args);
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
            directoryChildren: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["/"] = [temp.Root, "/tmp"],
                ["/tmp"] = [],
                [temp.Root] = [profile],
                [profile] =
                [
                    Path.Combine(profile, "projects"),
                    Path.Combine(profile, "state"),
                    Path.Combine(profile, ".local"),
                ],
                [Path.Combine(profile, "projects")] = [],
                [state] = [],
                [Path.Combine(profile, ".local")] =
                [
                    Path.Combine(profile, ".local", "bin"),
                    Path.Combine(profile, ".local", "share"),
                ],
                [Path.Combine(profile, ".local", "bin")] = [],
                [Path.Combine(profile, ".local", "share")] = [],
            }
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

    [Fact]
    public void A_guard_does_not_wrap_a_shell_that_does_not_exist()
    {
        using var temp = new TempDirectory();
        var guard = new BashGuard(
            Target(temp.Root),
            Fake.Unix(macOs: true, sandboxExec: true, shellExists: false)
        );

        var launch = guard.Wrap(Shell, Path.Combine(temp.Root, "ws"));

        Assert.False(launch.Wrapped);
        Assert.Equal("none", launch.Mechanism);
        Assert.Equal("/bin/bash", launch.FileName);
        Assert.Equal(["-c"], launch.ArgumentsPrefix);
        Assert.Null(launch.Notice);
    }

    [Fact]
    public void The_linux_plan_does_not_wrap_a_shell_that_does_not_exist_either()
    {
        using var temp = new TempDirectory();
        var guard = new BashGuard(
            Target(temp.Root),
            Fake.Unix(macOs: false, bwrap: "/usr/bin/bwrap", shellExists: false)
        );

        var launch = guard.Wrap(Shell, temp.Root);

        Assert.False(launch.Wrapped);
        Assert.Equal("none", launch.Mechanism);
        Assert.Equal("/bin/bash", launch.FileName);
        Assert.Equal(["-c"], launch.ArgumentsPrefix);
        Assert.Null(launch.Notice);
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
        var target = new GuardTarget(
            state,
            Path.Combine(state, "auth.json"),
            Path.Combine(temp.Root, "prefix"),
            temp.Root
        );
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
        new(
            Path.Combine(root, "state"),
            Path.Combine(root, "state", "auth.json"),
            Path.Combine(root, "prefix"),
            root
        );

    private static string ProfileArg(GuardCommand launch) => launch.ArgumentsPrefix[1];

    private static string Posix(string path) => path.Replace('\\', '/');
}

/// <summary>The probe fake, file-scope so sibling guard test classes reuse it.</summary>
internal static class Fake
{
    internal static GuardProbe Unix(
        bool macOs,
        bool sandboxExec = false,
        string? bwrap = null,
        string? setpriv = null,
        string bwrapOutput = "",
        string setprivOutput = "",
        bool? apparmorRestrictsUserns = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? directoryChildren = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? fileChildren = null,
        IReadOnlyList<string>? profileChildren = null,
        IReadOnlyList<string>? localChildren = null,
        bool authExists = false,
        bool shellExists = true,
        bool isWindows = false,
        Func<string, bool>? directoryExists = null
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
                path == BashGuardTests.Shell.ExecutablePath
                    ? shellExists
                    : sandboxExec && path == "/usr/bin/sandbox-exec"
                        || authExists && path.EndsWith("auth.json", StringComparison.Ordinal),
            DirectoryExists: directoryExists ?? (_ => true),
            GetEnvironmentVariable: _ => null,
            ReadTextFile: _ =>
                apparmorRestrictsUserns is not { } value ? null
                : value ? "1"
                : "0",
            ListDirectories: path =>
                directoryChildren is not null && directoryChildren.TryGetValue(path, out var mapped)
                    ? mapped
                : path.EndsWith(".local", StringComparison.Ordinal) ? localChildren ?? []
                : path is "/tmp" or "/var/tmp" ? []
                : profileChildren ?? [],
            ListFiles: path =>
                fileChildren is not null && fileChildren.TryGetValue(path, out var mapped)
                    ? mapped
                    : [],
            Run: (fileName, _) =>
                fileName.Contains("bwrap", StringComparison.Ordinal)
                    ? (bwrapOutput.Length == 0 ? 0 : 1, bwrapOutput)
                : fileName.Contains("setpriv", StringComparison.Ordinal)
                    ? (setprivOutput.Length == 0 ? 0 : 1, setprivOutput)
                : (0, "")
        );
    }
}

/// <summary>
/// The refusal's relative-path decision must anchor to the workspace, not to the process's current
/// directory (which in production is the workspace): the chdir test mutates process state, so it
/// never runs in parallel with another collection.
/// </summary>
[Collection(BashGuardCwdCollection.Name)]
public sealed class BashGuardProcessDirectoryTests
{
    [Fact]
    public void A_relative_redirect_is_never_refused_even_when_the_process_directory_is_denied()
    {
        using var temp = new TempDirectory();
        string state = Path.Combine(temp.Root, "state");
        string prefix = Path.Combine(temp.Root, "prefix");
        Directory.CreateDirectory(prefix);
        var target = new GuardTarget(state, Path.Combine(state, "auth.json"), prefix, temp.Root);
        var guard = new BashGuard(target, Fake.Unix(macOs: true, sandboxExec: true));
        var launch = guard.Wrap(BashGuardTests.Shell, Path.Combine(temp.Root, "ws"));

        string previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = prefix;

            Assert.Null(guard.Refusal("echo hi > rel/out.txt", launch));
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }
}
