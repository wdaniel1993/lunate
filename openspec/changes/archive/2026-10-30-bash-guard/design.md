## Context

The threat is persistence, not privacy: a command the user approves once can rewrite `trust.json`,
the install records or the `lunate` binary, and every later session runs that version silently. The
feasibility spike measured the OS options on macOS 26.3.1 (Seatbelt) and Linux (bubblewrap 0.9.0,
Landlock ABI 9): denying exactly the state root, the install prefix and `auth.json` reads refuses
those targets while `dotnet`, `git`, `npm`, `node` and `python3` keep working, at ~4.3 ms extra per
spawn on macOS. ADR-0024 records the decision.

## Decisions

### D1: A guard profile, not a jail

Only the persistence targets are denied; everything else keeps its ordinary behaviour. A jail that
constrains reads, network or the workspace would break the tools the guard exists to keep working,
and a guard that breaks `dotnet build` is a guard users disable.

### D2: The mechanism is per platform, and it is probed

- **macOS:** `sandbox-exec -p '<profile>' <shell> …` with `(allow default)` plus deny rules for
  `file-write*` under the state root and the install prefix and `file-read*` on `auth.json`.
  Profile paths are written canonically — Seatbelt matches resolved paths, so a symlinked spelling
  like `/var/…` would otherwise not be denied.
- **Linux, primary:** `bwrap --ro-bind / / --bind WS WS --tmpfs /tmp --dev-bind /dev /dev
  --ro-bind STATE STATE [--ro-bind NULLAUTH] --die-with-parent`, probed by running it once.
- **Linux, fallback:** `setpriv --landlock-access fs --landlock-rule path-beneath::…` with an
  allow-list subtraction walk, probed the same way.
- **Windows:** no mechanism. The tool result carries a one-time notice naming the gap and the
  command still runs — never silently unprotected in either direction.

### D3: Detected and reported, never assumed

Every resolution runs the chosen mechanism once (`/bin/true` under the actual profile). When `bwrap`
cannot start, the notice carries the probe's output; when
`/proc/sys/kernel/apparmor_restrict_unprivileged_userns` is `1`, the report names the AppArmor
restriction. "Installed but not working" is the failure the spike demonstrated in containers, and a
guard that silently does nothing is worse than no guard, because it is believed.

### D4: The refusal names the guard

A conservative pre-run scan refuses a command whose redirection (`>`/`>>` outside quotes, `~` and
`$LUNATE_HOME` spellings resolved) would land in a denied target, before the spawn, naming the guard
and mechanism. Obscured quoting, `$HOME` chains and non-redirection writes (`mv`, `cp`) are left to
the mechanism; on that path a failed run whose stderr shows a filesystem denial and whose command
mentions a denied target gets a hint naming the guard, marked best-effort.

### D5: Landlock's shape is honest

Landlock has no deny rules, so the fallback is allow rules with rights per rule, walking down from
the filesystem root: every directory that holds no denied target gets one full rule and its subtree
stays normal; the branches to the state root and install prefix grant listing and reads of the files
directly in them, and inside the targets the grants narrow to reads (plus execute in the prefix, so
`lunate` still runs). `auth.json` gets no rule anywhere. Landlock can only refuse writes it did not
grant, so creating a new file directly inside a directory on the path to a target stays denied rather
than granting make rights that would reach beneath it. The mechanism caps itself at 256 allow rules
and reports instead of ballooning the argv of a command spawn.

### D6: Wiring and the install prefix

The guard lives in `Lunate.Coding` and is wired inside `BashTool` — the only shell runner — as
internal types, no public API change. `ProcessRunner` and the tree kill are untouched:
`sandbox-exec` execs in place, so exit codes and kills behave. The install prefix is the directory of
a running binary when that binary is `lunate`; for dev processes (dotnet, test hosts) it is the
default install location `~/.local/bin`, because denying a build tree's own output directory would
break the tools the guard must keep working.

## Resume (session 2)

The first session stalled with the implementation green in a `wip(` commit; it was rewritten as
`feat(coding): bash guard profile for the shell tool`. A full local run then surfaced two
branch-caused regressions, each worked RED → GREEN:

- **The scan resolved relative redirects against the process's current directory.** When the process
  ran from the install-prefix directory, a legitimate workspace write
  (`mkdir … && printf … > src/ConsoleApp/newdir/Bad.cs`) was refused. Relative tokens now anchor to
  the shell's workspace, stored at resolution; absolute redirects into a denied target are still
  refused. RED: `RoslynFileBusSyncTests.A_new_file_in_an_untracked_directory_reaches_the_check_after_a_bash_project_change`;
  new pin `BashGuardProcessDirectoryTests` chdirs into a denied directory (silenced
  `bash-guard-cwd` collection) and requires the relative redirect to stay unrefused.
- **The guard wrapped a shell that does not exist**, so `sandbox-exec`'s `execvp` stderr replaced the
  tool's pinned "could not start" spawn error. `Wrap` now returns the shell unwrapped when its
  executable is missing. RED: `BashToolTests.A_shell_that_cannot_start_is_a_spawn_error`; new pins
  `A_guard_does_not_wrap_a_shell_that_does_not_exist` and the symmetric Linux `bwrap` test.

`BashGuard.cs` (545 lines, over the ~300-line guideline) was split into partials
(`BashGuard.Platforms`, `BashGuard.Scan`) with zero behaviour change. Full `Lunate.Coding.Tests` run
green after this: 2130/2130.

## Rulings

- **The Landlock fallback cannot express "deny only the state root"** (allow-list only, per
  `adr/notes-sandbox-feasibility.md`), so the fallback allow-lists the subtraction walk and documents
  the gaps: files directly in `$HOME` (e.g. `~/.gitconfig`) and `/tmp` stay denied when a denied
  target lives there, and `auth.json` stays readable under Landlock. Cost if wrong: users of the
  fallback see rare false denials instead of losing state-root writes.
- **The one-time "unavailable guard" notice prefixes tool results** on Windows and on Linux
  resolution failures; the handful of exact-output tests that see it are updated inline because the
  notice is the new pinned behaviour. Cost: noisy results on unsupported platforms, which is the
  point, not a defect.
- **Maintainer sub-requirements on #241 that were not in this card's enumerated scope** —
  `ToolRiskResolver` docker/podman flagging, the startup hardlink warning, the opt-out setting,
  `/context` mechanism reporting — are follow-up work, filed on the issue, not implemented here.
  Cost if wrong: they land later than the maintainer assumed — said on the issue and in the PR, not
  silent. **They are in scope for 0.2 and are tracked as #383.**

## Risks / Trade-offs

- **[A guard that silently does nothing is worse than no guard]** -> the probe runs the mechanism once
  and reports failures; an unavailable guard runs unconfined behind a visible notice.
- **[Landlock's allow-only shape cannot deny a single subtree]** -> the subtraction walk documents its
  gaps rather than pretending to a deny it cannot express.
- **[Docker and pre-existing hardlinks remain escapes]** -> the guard claims persistence protection,
  not containment; those are follow-ups (#381), not hidden assumptions.
- **[~4.3 ms per spawn]** -> measured in the spike; accepted for the protection it buys.
