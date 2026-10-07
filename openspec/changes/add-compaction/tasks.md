# Tasks

## 1. Compactor (TDD, red-first)

- [x] 1.1 Estimate + trigger: chars/4, usage correction, 80% threshold, window from catalog with documented fallback; `CompactNowAsync` forced path
- [x] 1.2 Reduction: keep-verbatim tail (system + last N turns, pair-safe boundary), fixed summarization prompt, same client; failure fail-open + reported; usage counted, not emitted
- [x] 1.3 Outputs: `AppendCompaction(summary, replaces)` (compile `replaces` from entries); `CompactionApplied` emitted with pinned fields; request rebuild (system + summary + tail); `ToHistory()` resume path verified for compacted sessions

## 2. Hook wiring

- [x] 2.1 `Compacting` seam invocation + adapter per T-37 contract (`Provide` wins; failure falls back); behavior unchanged unconfigured (test)

## 3. Tests

- [x] 3.1 Recorded long session: compacts once, next request < 60% window, replay matches; fixture committed + byte round-trip
- [x] 3.2 Boundary/failure matrix per design (pair safety, estimate correction, unknown window, forced, hook provide/fallback, resume after compaction)

## 4. Close

- [x] 4.1 `dotnet csharpier format .`; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-compaction --type change --strict`; self-review; commit per group
