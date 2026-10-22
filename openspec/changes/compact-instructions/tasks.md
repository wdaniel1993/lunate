# Tasks

## 1. Agent seam

- [x] 1.1 Red tests: `CompactNowAsync("focus on the flaky test")` sends a summarization request containing the focus text **and** all four fixed sections; `CompactNowAsync()` sends the fixed prompt with no focus block.
- [x] 1.2 Add `string? instructions = null` to `CompactNowAsync`, thread it through `ApplyCompactionAsync` into `CompactionReducer.ReduceAsync(messages, instructions, ct)`; the `IChatReducer` overload delegates with `null`.
- [x] 1.3 Record the signature in `PublicAPI.Unshipped.txt`; keep existing callers source-compatible.
- [x] 1.4 Commit: `feat(agent): accept focus instructions for a compaction`.

## 2. Isolation

- [x] 2.1 Red test: after a `/compact` with focus, an automatic compaction's request contains none of that text.
- [x] 2.2 Red test: the `compaction` session entry still holds only `summary` and `replaces`; the focus text appears nowhere in the session file.
- [x] 2.3 Commit: `test(agent): prove compaction focus never outlives its call`.

## 3. Command surface

- [x] 3.1 Red tests: `/compact` (empty) behaves as today; `/compact <text>` passes the text; 1,001 characters are refused with a notice naming the limit and nothing compacts; whitespace-only is treated as empty.
- [x] 3.2 Parse the argument in `InteractiveSession.Commands.cs` and call the new overload; keep `RefusedWhileBusy()` in front.
- [x] 3.3 Update `/help` and `docs/guide.md`.
- [x] 3.4 Commit: `feat(tui): pass /compact arguments to the summarizer`.

## 4. Verification

- [x] 4.1 Fast gate: `dotnet tool restore`, `dotnet csharpier check .`, `openspec validate compact-instructions --strict`, build, focused Agent/Coding suites.
- [x] 4.2 Full `scripts/verify.sh` on the final head.
- [x] 4.3 Commit: `docs(openspec): record the compact instructions verification`.
