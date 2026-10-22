# Tasks

## 1. Queue and indicator

- [x] 1.1 Red tests: a queued entry keeps text and attachments; the cap refuses the sixth entry with a notice and keeps the first five; the indicator shows the count and disappears at zero.
- [x] 1.2 Add the bounded queue to the interactive session and render `N queued` beside the mode hint, truncating at narrow widths.
- [x] 1.3 Golden fixtures at 40, 80 and 120 columns for 0, 1 and 5 queued; untouched fixtures stay byte-identical.
- [x] 1.4 Commit: `feat(tui): show and bound the follow-up queue`.

## 2. Queueing and draining

- [x] 2.1 Red tests: Enter while a turn runs queues and clears the line; Enter while idle submits; Alt+Enter, Shift+Enter and Ctrl+J insert a newline in both states.
- [x] 2.2 Red tests: when a run completes, the head of the queue runs through the normal submission path, one at a time and in order; a failed run does not discard the rest.
- [x] 2.3 Red test: an interrupted turn hands over to the queue once it has fully unwound.
- [x] 2.4 Red test: a slash command is refused by the queue with a notice and does not enter it.
- [x] 2.5 Implement the drain after every run outcome (completed, failed or interrupted; await the run task; never start a run while one is unwinding).
- [x] 2.6 Commit: `feat(tui): drain queued follow-ups after the turn`.

## 3. Taking a follow-up back

- [x] 3.1 Red tests: Up takes the whole queue back into the editor in order, newline-separated and ahead of the draft, and empties the queue; with an empty queue it navigates history.
- [x] 3.2 Red test: Esc with nothing running leaves the queue alone.
- [x] 3.3 Red test: Esc during a turn cancels the run and the queued messages then run in order.
- [x] 3.4 Commit: `feat(tui): take queued follow-ups back into the editor`.

## 4. Verification and documentation

- [x] 4.1 Composition test through the real host: start a slow turn, queue two messages, and prove both run in order afterwards with no overlap.
- [x] 4.2 Update the key table and the follow-up section in `docs/guide.md`; add the VHS tape.
- [x] 4.3 Amend the tui spec's key-table requirement in the same PR (the MODIFIED block travels with this change, not a later one).
- [x] 4.4 Fast gate: `dotnet tool restore`, `dotnet csharpier check .`, `openspec validate follow-up-queue --strict`, build, focused Tui/Coding suites.
- [x] 4.5 Full `scripts/verify.sh` on the final head.
- [x] 4.6 Commit: `docs(openspec): record the follow-up queue verification`.
- [x] 4.7 Rekey to Claude Code's mapping: Enter queues while a turn runs, Alt+Enter/Shift+Enter/Ctrl+J always insert a newline, Esc interrupts and hands over to the queue, and the prompt exposes no steering key.
- [x] 4.8 Replace Alt+Up with Claude Code's take-back: Up moves the whole queue into the editor ahead of the draft when entries wait, and stays history otherwise.
- [x] 4.9 Refuse a `/skill:` invocation while a turn runs (it starts a turn, so it cannot ride the queue).
