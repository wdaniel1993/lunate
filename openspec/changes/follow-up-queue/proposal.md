## Why

While a turn runs, the only thing the user can do with the input line is steer: Enter interrupts the model with new text. That is right when the new thought changes the current work, and wrong when it is simply the next thing to do — "run the tests after this", "then update the changelog". Today that means watching the turn finish before typing, or steering and hoping the model handles the interruption.

A follow-up queue separates the two: Enter still steers, Alt+Enter lines the message up for after the turn.

## What Changes

- **Enter while a turn runs** queues the current input — text and attachments — and clears the line. By default the send key never interrupts; the queue runs in order once the turn finishes, one message at a time.
- **Steering becomes a setting**: `prompt.busyEnter = steer` makes Enter inject into the running turn instead of queueing. The default is `queue`.
- **Alt+Enter and Ctrl+J are newlines in every state**, so a multi-line follow-up can still be composed while a turn runs.
- **Alt+Up** pulls the newest queued message back into the editor when the line is empty, so a queued mistake can be fixed before it runs.
- **Esc with nothing running and a non-empty queue** returns every queued message to the editor in order, separated by newlines.
- The prompt shows a **queue indicator** (`2 queued`) beside the mode hint, so a queue is never invisible.
- **Slash commands are refused** by the queue with a notice: they are frontend control and a queued `/quit` has no defined ordering against the running turn.
- The queue is **in-memory**: not in the session file, not replayed on resume, dropped on quit.

## What Does Not Change

- Enter still submits or steers exactly as today.
- A turn's failure or cancellation does not discard the queue; the next message runs, and a cancelled turn leaves the remaining messages for Alt+Up or the next run.
- The session format, the event stream and the extension surface are untouched.
