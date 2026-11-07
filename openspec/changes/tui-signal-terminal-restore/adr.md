# ADR Review Manifest

- Status: completed
- Review date: 2026-07-19

## Review Summary

ADR review completed for this change. The change touches the platform terminal-mode plumbing
within the boundary ADR-0019 already commits to (`IConsoleIO`/`src/Lunate.Tui/Platform` termios
and console-mode code) and reuses the existing raw-mode restore; no layering (ADR-0002), loop
(ADR-0003) or dependency change is involved, and no new packages are required (the signal
mechanism is the BCL's `PosixSignalRegistration`).

## In-Force ADRs Reviewed

- ADR-0004 (mintty input support) — terminal input plumbing precedent; the signal work stays
  behind the same `IConsoleIO`/platform seam.
- ADR-0007 (live area concurrency) — the handler never touches the render pipeline or the key
  channel; it only restores termios and returns.
- ADR-0019 (terminal UI choice) — confirms `src/Lunate.Tui/Platform` as the home of the termios
  code the fix serialises and that tests drive real platform behaviour through libc.

## New Durable ADRs Created

- None - no major durable architectural decision was introduced: the fix registers platform
  signal handlers that reuse the existing restore instance, within ADR-0019's committed contract.
