# Bash guard: deny the state root and the install prefix

Refs #241 (card 0.2, bash guard profile), merged as `06a0b8bd` (#313) on 2026-10-30.

> This change was implemented before it was written down: the branch merged with the spec delta
> hand-edited into `openspec/specs/agent-tools/spec.md` and its ledger in `.superpowers/`. It is
> reconstructed here, after the fact, so the change history, the design and the delta live where
> every other change keeps them — and so the archived delta can be checked against the spec it
> produced. Nothing in this folder changes behaviour; the code and the spec are already on `main`.

## Why

The bash tool runs model-chosen commands with the user's rights. The approval panel is the boundary
in default mode, but a command the human approved must not be able to make itself forgotten: one
approved command can rewrite `trust.json`, install records, or replace the `lunate` binary, and every
later session runs it silently. The state root is not a security boundary.

## What Changes

- The `bash` tool wraps its child process in a **guard profile, not a jail**: writes under the state
  root (`LUNATE_HOME`, default `~/.lunate`) and under the install prefix are denied at the OS level,
  `auth.json` under the state root is read-denied, and every other access keeps its ordinary
  behaviour.
- Mechanisms per platform: `sandbox-exec` on macOS, `bwrap` on Linux with `setpriv`'s Landlock rules
  as the fallback; Windows has no mechanism and says so instead of running unconfined silently.
- Every resolution **runs the mechanism once** before use, so "installed" cannot stand in for
  "working" — including the AppArmor restriction of unprivileged user namespaces on Ubuntu 23.10+.
- A redirection that would write a denied target is refused **before the spawn**, naming the guard
  and its mechanism; a failed command that hit a filesystem denial on a denied target carries a
  best-effort hint.
- Tests pin the refusals, the normal-command path and the platform matrix; ADR-0024 records the
  decision.

## Impact

`Lunate.Coding` (`BashTool` and the guard's internal types), the `agent-tools` spec's `Bash guard`
requirement, and the platform matrix in the gate. No public API change, no session-format change.

Deferred and filed as #380-style follow-ups in the 0.2 milestone: docker/podman state-root mounts as
high-risk in `ToolRiskResolver`, the startup hardlink warning, the opt-out setting, and the active
mechanism in `/context` and the header.
