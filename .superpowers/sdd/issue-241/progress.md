# SDD ledger — plan: card 0.2 bash guard profile (#241)

## Plan
The card is the plan: implement the bash guard profile (macOS `sandbox-exec`, Linux `bwrap`
with Landlock (`setpriv`) fallback), Windows unsupported-with-notice, refusal messages that
name the guard, plus tests (state-root write refused; normal command succeeds; platform
matrix in the gate; Linux detection of restricted user namespaces) and the ADR the issue
asks for. Tests first (RED), then implementation (GREEN).

## Design decisions
- Guard lives in `Lunate.Coding`, wired inside `BashTool` (the only shell runner); new
  internal types only — no public API change.
- Targets: state root (`LunateHome.Store`), install prefix (directory of the running
  binary), `auth.json` read-deny under the state root.
- macOS: `sandbox-exec -p '<profile>' <shell> …`; profile is `(allow default)` + deny
  file-write* on state root + prefix + deny file-read* literal auth.json.
- Linux, primary: `bwrap --ro-bind / / --bind WS WS --tmpfs /tmp --dev-bind /dev /dev
  --ro-bind STATE STATE [--ro-bind NULLAUTH] --die-with-parent`, probed once at resolve
  time by actually running it.
- Linux, fallback: `setpriv --landlock-access fs --landlock-rule path-beneath::…` with an
  allow-list "subtraction walk" (siblings of every denied target); availability probed by
  running it.
- Linux restriction detection: when the bwrap probe fails, read
  `/proc/sys/kernel/apparmor_restrict_unprivileged_userns`; when it is 1 the notice names
  AppArmor restriction — detected and reported, never assumed away. When bwrap is missing
  or fails for another reason, the same notice carries the probe output.
- Windows: no guard; the tool result carries a one-time notice naming the gap, the command
  still runs (the issue's failure behaviour: never silently unprotected in either direction).
- Pre-run refusal: a conservative redirect-target scan (`>`/`>>` outside quotes with a
  token that normalizes into a denied target) refuses BEFORE the spawn and names the guard;
  the OS guard remains the backstop for everything the scan misses.
- Post-run hint: on a failed run whose stderr shows a filesystem denial (Operation not
  permitted / Permission denied / Read-only file system) and whose command mentions a
  denied target, append a hint naming the guard and the mechanism.

## Rulings
- Ruling: the Landlock fallback cannot express "deny only the state root" (allow-list only,
  per spike/notes-sandbox-feasibility.md), so the fallback allow-lists the subtraction walk
  and documents the gaps: files directly in `$HOME` (e.g. `~/.gitconfig`) and `/tmp` stay
  denied when a denied target lives there, and `auth.json` stays readable under Landlock.
  Cost if wrong: users of the fallback see rare false denials instead of losing state-root
  writes.
- Ruling: the one-time "unavailable guard" notice for Windows (and Linux resolution
  failures) prefixes tool results; the handful of exact-output tests that see it are
  updated inline because the notice is the new pinned behaviour. Cost: noisy results on
  unsupported platforms, which is the point, not a defect.
- Ruling: maintainer sub-requirements on #241 that are not in this card's enumerated scope
  (`ToolRiskResolver` docker/podman flagging, the startup hardlink warning, the opt-out
  setting, `/context` mechanism reporting) are follow-up work, filed on the issue, not
  implemented here. Cost if wrong: they land later than the maintainer assumed — said on
  the issue and PR, not silent.
