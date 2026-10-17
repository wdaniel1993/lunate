# Design — roslyn-refresh-rebaseline

## Context (the exact sequence)

1. The user edits `Calculator.cs` → the file-change bus delivers the mark **and** the stamp scan sees a changed `(mtime, length)` stamp.
2. `SyncChangedDocuments` → `DocumentFreshness.TakeChanged` reports the file **and re-baselines** the stamp to the current (post-edit) state.
3. `RefreshDocuments` re-reads the text, applies it via `Workspace.TryApplyChanges` — which **writes the refreshed text back to disk and bumps the mtime** after step 2's baseline.
4. The next `changed` check compares the post-write-back stamp against the baseline from step 2 → different → the file re-flags. Each refresh repeats the bump, so this never stabilizes.

Confirmed on the pre-fix tree (failing test, reproduced 2026-10-17 before any fix).

## Decision — re-baseline after the apply

After the `TryApplyChanges` step in `RefreshDocuments`, re-take each refreshed path's stamp (`DocumentFreshness.Track`). The next scan then compares against the post-write-back state; with no further edits it reports nothing.

Notes:

- **Idempotency preserved**: the bus-mark + stamp-scan merge still yields one count per call for a file flagged by both paths; the change only stops the phantom count after a refresh.
- **Race window**: a user edit landing between the scan's baseline and the write-back would be re-baselined over — but the bus mark for that edit is delivered separately and drained on the next call, so the class of change the stamps cannot see (same-stamp rewrites) is still caught by its designated path; the stamp backstop is unchanged for out-of-band edits after step 4's state.
- **Apply failure**: unconditional re-baseline is safe — a failed apply did not write back, so re-taking the stamp reproduces the scan's own baseline.

## Alternatives considered

- **Avoid the write-back (forked snapshot path)**: larger surface change to the refresh design whose fallback path deliberately compiles the forked snapshot; not needed — the write-back itself is harmless, only the stale baseline was wrong.
- **Ignore mtime-only differences in the stamp**: would weaken the stamp scan for legitimate out-of-band touch/rewrite cases and change `DocumentFreshness` semantics globally; rejected.
