## 1. Queued follow-up visibility

- [ ] 1.1 Red renderer test: a state with a queued count and a first line renders `N queued` plus the clipped line, and clips rather than wraps at every width band.
- [ ] 1.2 Red session test: queueing a follow-up passes the first line of the head entry to the live area; taking the queue back clears it.
- [ ] 1.3 `LiveArea.SetQueued(int count, string? first)` replaces `SetQueuedCount`, with `LiveAreaState.QueuedFirst`; `StatusFooterRenderer` renders the segment.
- [ ] 1.4 `InteractiveSession.UpdateFollowUpIndicator` passes the head entry's first editor line.

## 2. Alt-free routes

- [ ] 2.1 Red routing tests: `Ctrl+Left`/`Ctrl+Right` produce word-left/word-right, `Ctrl+Delete` produces kill-word-forward.
- [ ] 2.2 Catalog rows beside the Alt rows, same contexts, descriptions that name the Alt chord they mirror.
- [ ] 2.3 Assert the three bindings that already have an Alt-free route gained no second row.

## 3. Gate

- [ ] 3.1 `openspec validate prompt-workflow-residuals --strict`; `dotnet csharpier check .`; Release build; focused Tui and Coding classes.
- [ ] 3.2 Full `scripts/verify.sh` before ready-for-review; `ci-ok` green before merge; archive as the final commit of the same PR.
