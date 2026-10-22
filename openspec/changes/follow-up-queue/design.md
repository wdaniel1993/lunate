## Context

`InteractiveSession.SubmitAsync` has one busy path: while a turn runs, text goes to steering. The input line holds text plus attachments and is cleared on submit. There is no place to keep "the next message" today, and no key is bound to one.

## Decisions

**The queue is a bounded list of prepared inputs, owned by the session and never persisted.** It holds the same shape the prompt submits (text plus attachments), so replaying one goes through the identical submission path and inherits approval, image handling and command dispatch. Five entries is the cap: a sixth is refused with a notice rather than silently dropping the oldest — a dropped follow-up is lost work, and the user is present to act on the notice.

**Enter queues by default; steering is a setting.** The send key is the one affordance every user already presses, and on Termux it is the only reliable one (Alt chords are scarce on a phone keyboard). The common case — "here is the next thing to do" — therefore gets the plain key, and a message typed during a turn never silently interrupts the model. Steering remains reachable through `prompt.busyEnter = steer` (env `LUNATE_BUSY_ENTER`), which makes Enter inject instead of queue; under that setting the queue is unreachable and stays empty, because there is no second key to fill it. Alt+Enter and Ctrl+J are newlines in every state, which keeps multi-line composition working mid-turn — the thing pi solves with Shift+Enter and this product cannot rely on across terminals.

**The queue drains after the turn, not during it.** When a run completes — successfully or not — the session takes the head of the queue and runs it as if the user had just submitted it. One at a time, in order: running two at once would make the transcript unreadable and the second would fight the first for the same tool approvals. A failed run does not discard the rest; each entry is an independent instruction the user chose to send.

**Alt+Up pulls the newest entry back, only into an empty line.** Merging a queued message into half-typed text would silently produce something the user never wrote, so the key refuses when the line is not empty. Pulling the newest first makes repeated presses walk backwards through the queue.

**Esc returns the whole queue.** Esc already means "cancel" while a turn runs, and that is unchanged: the turn stops and the queue stays. When nothing is running and the queue is not empty, Esc is the only key left that means "take this back", so it restores every entry to the editor in order, newline-separated, and empties the queue. That is the recovery path after a cancel.

**Commands are not queueable.** A slash command is frontend control that runs immediately and never reaches the model; queueing one would mean either running it mid-turn (unclear ordering) or deferring a `/quit` past the turn it was meant to end. Refusing with a notice is honest about the limit.

## Risks

- **Indicator overlap.** The queue indicator shares the line beside the mode hint; at narrow widths it must truncate without pushing the hint off the row. Golden fixtures cover 40, 80 and 120 columns.
- **Auto-drain races.** The drain must not start a second run while the previous one is still unwinding; the harness already refuses a concurrent run, so the drain waits on the run task rather than polling.
- **Cancellation.** A cancelled turn must leave the queue intact and must not auto-drain, otherwise Esc would launch the next message immediately after the user cancelled.

## Public surface

No change to the session schema or the extension surface. The queue lives in the interactive session only; print mode has no input line and therefore no queue.
