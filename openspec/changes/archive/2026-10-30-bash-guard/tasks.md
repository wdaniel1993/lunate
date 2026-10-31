## 1. The guard and its mechanisms

- [x] 1.1 RED: a state-root write is refused and the file never lands; a normal workspace command
      runs unchanged. (`BashGuardToolTests`)
- [x] 1.2 Implement `BashGuard` in `Lunate.Coding` and wire it inside `BashTool`'s spawn — the only
      shell runner — as internal types, no public API change. (commit `feat(coding): bash guard
      profile for the shell tool`)
- [x] 1.3 macOS: `sandbox-exec` with an inline `(allow default)` profile, deny `file-write*` on the
      state root and install prefix, deny `file-read*` on `auth.json`, canonical paths.
- [x] 1.4 Linux primary: `bwrap` with the workspace writable, `--tmpfs /tmp`, `/dev/null` bound over
      `auth.json`, `--die-with-parent`.
- [x] 1.5 Linux fallback: `setpriv --landlock-access fs` with the allow-list subtraction walk, the
      256-rule cap, and the documented gaps.
- [x] 1.6 Windows: no mechanism; a one-time notice names the gap and the command still runs.

## 2. Probe, refusal and hint

- [x] 2.1 Every resolution runs the mechanism once (`/bin/true` under the real invocation); a probe
      failure reports its output, and `apparmor_restrict_unprivileged_userns=1` is named.
- [x] 2.2 A conservative pre-run redirect scan refuses a denied target before the spawn, naming the
      guard and its mechanism. (commit `feat(coding): bash guard profile for the shell tool`)
- [x] 2.3 A failed run whose stderr shows a filesystem denial on a denied target carries a
      best-effort hint naming the guard.

## 3. Regressions found by the full run

- [x] 3.1 Anchor relative redirect tokens to the shell's workspace instead of the process's current
      directory; keep absolute refusals. RED → GREEN with
      `BashGuardProcessDirectoryTests` (`bash-guard-cwd`, non-parallel) as the new pin.
- [x] 3.2 Never wrap a shell whose executable is missing, so the tool's pinned spawn error survives
      `sandbox-exec`'s `execvp`. RED → GREEN with `A_guard_does_not_wrap_a_shell_that_does_not_exist`
      and the Linux symmetric test.
- [x] 3.3 Split `BashGuard.cs` (545 lines) into `BashGuard.Platforms.cs` and `BashGuard.Scan.cs` under
      the ~300-line guideline, zero behaviour change.

## 4. Spec, ADR and validation

- [x] 4.1 ADR-0024 records the guard, the mechanisms, the probe rule and the Landlock shape.
      (`adr/0024-bash-guard.md`)
- [x] 4.2 The `agent-tools` spec gains the `Bash guard` requirement with six scenarios; this change's
      delta is that requirement, unchanged.
- [x] 4.3 `Lunate.Coding.Tests` green at 2130/2130 after the regressions were fixed; the platform
      matrix runs the guard on macOS and Linux in CI.
- [x] 4.4 Archive this change after the merge and sync the delta into
      `openspec/specs/agent-tools/spec.md` — the reconstruction of that step is this folder.
