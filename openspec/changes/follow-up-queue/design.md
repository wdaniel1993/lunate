## Context

`InteractiveSession.SubmitAsync` has one busy path: while a turn runs, text goes to steering. The input line holds text plus attachments and is cleared on submit. There is no place to keep "the next message" today, and no key is bound to one.

## Decisions

**The queue is a bounded list of prepared inputs, owned by the session and never persisted.** It holds the same shape the prompt submits (text plus attachments), so replaying one goes through the identical submission path and inherits approval, image handling and command dispatch. Five entries is the cap: a sixth is refused with a notice rather than silently dropping the oldest — a dropped follow-up is lost work, and the user is present to act on the notice.

**Enter queues; there is no steering key.** Claude Code, which most desktop users arrive from, documents exactly this: type a message and press Enter while the agent works and the message is queued instead of interrupting the turn. The send key is also the only affordance that survives every terminal, including Termux, where Alt chords are scarce. Alt+Enter, Shift+Enter (where the terminal reports extended keys) and Ctrl+J are therefore newlines in every state, so multi-line composition works mid-turn. Steering stays in the harness API — the loop still accepts it — but the interactive prompt no longer exposes it, because "Enter does two things depending on state" is the ambiguity this change exists to remove.

**The queue drains after the turn, not during it.** When a run completes — successfully or not — the session takes the head of the queue and runs it as if the user had just submitted it. One at a time, in order: running two at once would make the transcript unreadable and the second would fight the first for the same tool approvals. A failed run does not discard the rest; each entry is an independent instruction the user chose to send.

**Alt+Up pulls the newest entry back, only into an empty line.** Merging a queued message into half-typed text would silently produce something the user never wrote, so the key refuses when the line is not empty. Pulling the newest first makes repeated presses walk backwards through the queue.

**Esc interrupts, and the queue goes next.** An interrupt means "stop this and get to what I already said", so the interrupted turn hands over to the queue rather than stranding it — the same rule Claude Code states for its own queue. When nothing is running and the queue is not empty, Esc is instead the only key that means "take this back", so it restores every entry to the editor in order, newline-separated, and empties the queue.

**Commands are not queueable.** A slash command is frontend control that runs immediately and never reaches the model; queueing one would mean either running it mid-turn (unclear ordering) or deferring a `/quit` past the turn it was meant to end. Refusing with a notice is honest about the limit.

## Risks

- **Indicator overlap.** The queue indicator shares the line beside the mode hint; at narrow widths it must truncate without pushing the hint off the row. Golden fixtures cover 40, 80 and 120 columns.
- **Auto-drain races.** The drain must not start a second run while the previous one is still unwinding; the harness already refuses a concurrent run, so the drain waits on the run task rather than polling.
- **Interrupt timing.** The hand-over must happen after the interrupted run has fully unwound, never while its task is still cancelling; the drain awaits the run task, so the next turn cannot start inside the cancellation.

## Public surface

No change to the session schema or the extension surface. The queue lives in the interactive session only; print mode has no input line and therefore no queue.
