# ADR-0024: The bash guard — an OS-level deny for the state root and the install prefix

- Status: accepted — 2026-10-29 (from the sandbox feasibility spike, card `t_0cc0fd09` / #241; spike note `adr/notes-sandbox-feasibility.md` on `spike/sandbox`)
- Context: the threat is persistence — one approved command can rewrite `trust.json`, install records, or replace the `lunate` binary, and every later session runs it silently
- Considers: `docs/guide.md` §Security and safety, "the state root is not a security boundary"

## Context

The bash tool runs model-chosen commands with the user's rights. The approval panel is the
boundary in default mode, but a command the human approves must not be able to make itself
forgotten. The feasibility spike measured the OS options on macOS 26.3.1 (Seatbelt) and
Linux (bubblewrap 0.9.0, Landlock ABI 9): a **guard** — deny exactly the state root, the
install prefix and `auth.json` reads — refuses the targets while `dotnet`, `git`, `npm`,
`node` and `python3` keep working, at ~4.3 ms extra per spawn on macOS. Admins' list on what
a sandbox cannot stop stands: a reachable Docker daemon socket, a **pre-existing hardlink**
into the state root, the workspace itself; the spike's escape table shows the Docker socket
returns a write from inside a macOS jail, so the guard claims persistence protection, not
containment.

## Decision

**The bash tool wraps its child process with a guard profile, not a jail, denying writes
under the state root (`LUNATE_HOME`, default `~/.lunate`) and the install prefix, plus
read-deny on `auth.json`.**

- **Mechanisms per platform:** `sandbox-exec` on macOS (inline `(allow default)` profile
  with the deny rules; profile paths are written canonically — Seatbelt matches resolved
  paths, so a symlinked spelling like `/var/…` would otherwise not be denied and reads
  through `cat` would succeed); `bwrap` on Linux (`--ro-bind / /` with the workspace
  writable, `--tmpfs /tmp` — MSBuild dies without a writable temp dir — and `/dev/null`
  bound read-only over `auth.json` so its reads see an empty file); `setpriv`'s Landlock
  rules (`--landlock-access fs` with allow rules) as the Linux fallback when user
  namespaces do not work. Windows is unsupported: there is no cheap unprivileged
  equivalent that can deny one subtree, so the tool says so in its result rather than
  silently running unconfined.
- **Detected and reported, never assumed.** Every resolution runs the chosen mechanism
  once (`/bin/true` under the actual profile). When bubblewrap cannot start, the notice
  carries the probe's output; when
  `/proc/sys/kernel/apparmor_restrict_unprivileged_userns` is `1` (Ubuntu 23.10+/24.04
  AppArmor), the report names the restriction. "Installed but not working" is the failure
  the spike demonstrated in containers; the guard that silently does nothing is worse
  than no guard, because it is believed. An unavailable guard runs the command unconfined
  behind a visible one-time notice — windows say the unsupported line, Linux names its
  probe failure.
- **The refusal names the guard.** A command whose redirection would land in a denied
  target (`>`/`>>` outside quotes, `~` and `$LUNATE_HOME` spellings resolved) is refused
  before the spawn with a message naming the guard and mechanism. The scan is
  conservative on purpose — obscured quoting, `$HOME` chains and non-redirection writes
  (`mv`, `cp`) are left to the mechanism, and on that path the tool appends a hint naming
  the guard to a result that failed with a filesystem denial. Both secrets are known to
  the model, not to the shell's raw errno.
- **Landlock's shape is honest.** Landlock has no deny rules, so the fallback allow-lists
  the subtraction walk: every sibling of every denied target at every level, plus the
  workspace, the temp roots and `/dev/null`. Its gaps are stated, not hidden: files
  directly in `$HOME` (e.g. `~/.gitconfig`) stay unwritable when a denied target is a
  `$HOME` child, `auth.json` stays readable (Linux Landlock cannot handle read rights, so
  the write-deny is what the fallback buys), and the mechanism caps itself at 64 allow
  rules and reports instead of ballooning the argv of a command spawn.
- **Where it is wired:** inside `BashTool`, which owns the shell spawn and the tool
  result text; `ProcessRunner` and the tree kill are untouched (sandbox-exec execs in
  place, so exit codes and kills behave; observed in the spike and covered by the
  existing timeout tests, which now run under the guard on macOS).
- **The install prefix follows the install shape:** the directory of a running binary
  when that binary is `lunate`; for dev processes (dotnet, test hosts) the default
  install location `~/.local/bin` instead, because denying a build tree's own output
  directory would break the tools the guard is supposed to keep working.

## Consequences

- One approved command can no longer rewrite `trust.json`, `settings.json`, install
  records, sessions or replace the `lunate` binary, on macOS and on Linux with either
  mechanism. The approval panel stays the boundary for reading the state root.
- Normal cost is one spawn step (~4.3 ms measured on macOS) plus one mechanism probe per
  session; a bash result carries no extra text on an active guard.
- `ps`/`top` do not work inside a Seatbelt sandbox (a property of the OS mechanism, not
  of the profile); their broken output is the sandbox's, and `pgrep`/`lsof` still work.
- `sandbox-exec` is deprecated: it works on 26.3.1 and has no CLI-shaped replacement, but
  the plan for the day Apple removes it is to degrade the mechanism to the Linux path —
  the guard resolution already reports, notices and notices, and falling back to "no
  guard on macOS, said in the tool result" keeps the property that matters: never
  silently unconfined.
- A bash command's spawn inherits the guard (`lunate trust`/`install` from bash fails
  cleanly rather than writing `trust.json` — the same rule the `LUNATE_SESSION` marker
  enforces in text).
- Out of scope, followed up on #241: `ToolRiskResolver` flagging of docker/podman
  commands that mount the state root, the startup hardlink warning, the opt-out setting,
  and the mechanism's name in `/context` and the header.
