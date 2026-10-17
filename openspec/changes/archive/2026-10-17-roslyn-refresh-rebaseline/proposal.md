# Roslyn refresh re-baseline: a refreshed file must not re-flag

## Why

`cs_diagnostics` with scope `changed` refreshes files the user edited, and the refresh applies the new text through `Workspace.TryApplyChanges` — which writes the refreshed text back to disk and bumps the file's mtime *after* the stamp scan already re-baselined. Every following `changed` check therefore sees an mtime difference against the baseline, re-flags the same file, refreshes it again (bumping the mtime again) and reports "N file(s) changed" forever — the diagnostics never stabilize and the noise repeats on every call.

Confirmed before fixing (card `t_6fcf625a`, the demanded pre-fix reproduction): a new integration test edits one file, runs `cs_diagnostics` twice with no edits in between — the first check reports `1 file(s) changed since the last check`, the second reports **exactly the same** instead of `no files changed since the last check` (`RoslynBackendSyncTests.A_refresh_does_not_reflag_the_file_on_the_next_check`, failing on the current tree).

## What Changes

- **Re-baseline the freshness stamps after the refresh apply**: once the refreshed text has been applied (the step that writes back and bumps mtimes), the backend re-takes each refreshed path's stamp, so the next scan compares against the post-write-back state. Each call then still reports at most one count per file (the `roslyn-file-bus-sync` idempotency rule is untouched); a check with no further edits reports none.
- **Regression test**: the reproduction test above stays as the pinned regression (red before the fix, green after).
- **Spec delta**: `agent-tools` — MODIFIED "C# diagnostics tool": a refresh SHALL NOT re-flag the refreshed file (new scenario).

## Impact

- `src/Lunate.Roslyn/RoslynBackend.cs` (refresh path, ~4 lines + comment); `tests/Lunate.Roslyn.Tests/RoslynBackendSyncTests.cs` (+1 test); `openspec` delta. No new packages, no ADR (existing seam), no public surface change.
