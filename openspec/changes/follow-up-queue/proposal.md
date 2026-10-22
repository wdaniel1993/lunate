## Why

While a turn runs, the only thing the user can do with the input line is steer: Enter interrupts the model with new text. That is right when the new thought changes the current work, and wrong when it is simply the next thing to do — "run the tests after this", "then update the changelog". Today that means watching the turn finish before typing, or steering and hoping the model handles the interruption.

A follow-up queue separates the two: Enter still steers, Alt+Enter lines the message up for after the turn.

## What Changes

- **Enter while a turn runs** queues the current input — text and attachments — and clears the line. The send key never interrupts; the queue runs in order, one message at a time, when the turn completes, fails, or is interrupted. This is Claude Code's documented behaviour, and the one most desktop users arrive with.
- **Steering leaves the prompt.** Its harness API stays, but the interactive surface no longer has a key for it: the ambiguity of "Enter does one thing when idle and another while busy" is what this change removes.
- **Alt+Enter, Shift+Enter (where the terminal reports it) and Ctrl+J are newlines in every state**, so a multi-line follow-up can still be composed while a turn runs.
- **Up** takes the whole queue back into the editor, newline-separated and ahead of any draft, so a queued mistake can be fixed before it runs; with an empty queue it navigates history as before.
- The prompt shows a **queue indicator** (`2 queued`) beside the mode hint, so a queue is never invisible.
- **Slash commands are refused** by the queue with a notice: they are frontend control and a queued `/quit` has no defined ordering against the running turn.
- The queue is **in-memory**: not in the session file, not replayed on resume, dropped on quit.

## What Does Not Change

- Enter still submits or steers exactly as today.
- A turn's failure or cancellation does not discard the queue; the next message runs, and a cancelled turn leaves the remaining messages for Alt+Up or the next run.
- The session format, the event stream and the extension surface are untouched.
