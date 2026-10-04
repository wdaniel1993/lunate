# 0004 — Windows terminals: Windows Terminal primary, Git Bash (mintty) best-effort

- Status: proposed — awaiting maintainer sign-off and the S-2 manual mintty run
- Date: 2026-10-04
- Spike: `docs/spikes/S-2/report.md` (automated evidence under `docs/spikes/S-2/evidence/`)

## Context

Lunate is a terminal TUI and its input line (T-18) reads VT raw keys. The
guide's Windows terminal matrix wants Git Bash (mintty) to work, but mintty
does not allocate a Windows console for native console processes: historically
`Console.ReadKey`/`Console.KeyAvailable` fail there with
`InvalidOperationException` ("…does not have a console…"). The guide asks for
an early test and a documented fallback (Windows Terminal).

S-2 built a throwaway probe (`docs/spikes/S-2/RawKeys`) with a headless VT
decoder, a console-capability probe, and a `Console.ReadKey` interactive mode.
Automated macOS evidence (no TTY) reproduces the failure class: when stdin is
not a console, `Console.KeyAvailable` throws that exact exception, while the
VT decoder still handles redirected input deterministically (32/32 self-test,
37/37 bytes across arrows, function keys, modifiers, and UTF-8). The manual
check on a real Git Bash (mintty) window on Windows — direct, `winpty`,
optional `MSYS=enable_pcon`, and Windows Terminal as control — is specified in
the report and has not run yet.

## Decision (proposed)

- **Interactive input is read through Lunate's own VT input layer, not
  `Console.ReadKey`.** The spike's `ReadKey` mode exists to measure the legacy
  API; the product path must tolerate terminals that have no Windows console
  (T-18 already plans VT raw keys).
- **Windows Terminal is the primary supported Windows terminal.** It is the
  guide's default and the control environment for the manual check.
- **Git Bash (mintty) is supported best-effort.** Shell integration (`bash.exe`
  invocation) is unaffected. For the interactive TUI, the manual run decides
  which launch mode is documented: direct if the probe shows a console,
  otherwise `winpty`/ConPTY if they work. If none works, mintty users get a
  clear one-line diagnostic pointing at Windows Terminal instead of a crash.
- **Fail soft, always**: when no console is attached, the TUI detects it and
  explains the fallback rather than throwing.

## Alternatives considered

- **Depend on `Console.ReadKey` as the input API**: rejected — it needs a real
  Windows console, fails under mintty and under any redirected stdin, and
  cannot be the base of the TUI's raw-key handling.
- **Drop Git Bash/mintty support entirely**: rejected — Git Bash is the guide's
  first-choice Windows shell and users will run the TUI from it; a hard "no"
  is worse than a guided fallback.
- **Require `winpty` for mintty**: rejected as a hard requirement (extra
  moving part, deprecation risk); keep it as a documented workaround only if
  the manual run shows it is the working path.

## Consequences

- The TUI input layer owns VT decoding and terminal-capability detection; the
  spike's decoder is a reference, not shipped code.
- The guide's manual terminal matrix gains a mintty row with the observed
  outcome once the manual run lands.
- Windows users without Windows Terminal get an actionable diagnostic.
- Re-open when Git for Windows enables ConPTY by default for native console
  apps, or when the manual run shows direct mintty support.
