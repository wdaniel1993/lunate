# Design — roslyn-refresh-rebaseline

## Context — measured, not assumed

A probe (temporary test, run 2026-10-17) dumped the disk stamp + content hash of a file around every refresh step. Findings:

1. The user's edit: mtime + length change (stamp visible) — the existing trigger path.
2. The refresh's `Workspace.TryApplyChanges`: **writes the refreshed text back with byte-identical content and a fresh mtime**, on every apply (measured twice: user-edit refresh and a bus-marked same-content rewrite). The sub-millisecond mtime bump lands after the stamp scan already re-baselined, so every subsequent `changed` check re-flagged the file — confirmed reproduction: second check with no edits reported `1 file(s) changed since the last check` instead of `no files changed…`.
3. Byte fidelity: the write-back preserved the bytes exactly (`sha` identical in the probe), so comparing the disk text against the applied text is a reliable guard.

## Decision — three-phase refresh

`RefreshDocuments` runs in three phases:

1. **Read + stamp**: per path, stamp → read → stamp. A stamp that moves across the read means a write raced the read; the path is skipped this round (the scan baseline still differs from disk, so it re-flags on its own).
2. **Revalidate + apply**: immediately before applying, each read's stamp is re-checked against the disk. Only still-matching reads enter the solution and the `TryApplyChanges` batch — so the write-back cannot **clobber** a write that landed after the read (the racing write stays on disk untouched and re-flags next check; the workspace stays stale for exactly one check).
3. **Verify + re-baseline**: after the apply (which bumps mtimes), the baseline is re-taken **only when the disk still holds exactly the text that was applied**, stamped on both sides of the verification read and recorded as that verified stamp directly (never via a fresh `stat`, which could absorb a racing write). A mismatch (a write racing the apply) leaves the old baseline in place, so the next scan re-flags instead of swallowing — and once its content is refreshed, the file converges.

## Why not "baseline := the stamp captured at read time"

Measured: the write-back changes the mtime of every refreshed file. A read-time stamp as the new baseline would therefore reproduce the phantom re-flag (current mtime ≠ read-time mtime) — the exact bug being fixed. The correct baseline value is the *verified* post-apply state: re-taken only after proving the disk still holds the applied content, so it describes "the content we applied" (the maintainer's requirement) without becoming a blind post-apply `stat` that can absorb a foreign write (the swallowing hazard).

## Residual windows (documented)

- A write landing in the microseconds between phase 2's validation stat and the batched apply of that same path — inherent to the batched `TryApplyChanges` API (per-path atomicity is not available); the window is orders of magnitude smaller than the file operations themselves, and the bus mark (tool edits) still flags it.
- A *same-content* rewrite racing the refresh is invisible to the stamps by construction (the documented `DocumentFreshness` granularity; the bus covers the tool-side case — see `A_bus_mark_reports_a_stamp_invisible_rewrite`).

## Test seam

`internal RoslynBackend(string, Func<BootstrapResult>, Action<string>? onRefreshRead = null)` — the hook fires with each path the refresh just read and stamped, before the apply. Tests use it to inject a deterministic racing write; the public constructor is unchanged (`null` hook).

## Tests

- `A_refresh_does_not_reflag_the_file_on_the_next_check` — the reproduction: check → check with no edits → `no files changed`.
- `An_edit_racing_the_refresh_re_flags_via_the_stamp_scan_and_is_not_clobbered` — the racing write (bash-style, no bus) survives on disk, re-flags on the next check, converges after.
- `An_edit_racing_the_refresh_re_flags_via_the_bus_and_is_not_clobbered` — same via `NotifyFileChanged`.
