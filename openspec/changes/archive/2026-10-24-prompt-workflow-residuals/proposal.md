# Prompt workflow residuals

## Why

P5 (prompt workflow) is almost entirely shipped: `!`/`!!` bash mode, the bounded follow-up queue with take-back, steering drained at the top of each model call, and `/compact <instructions>` all exist and are specified. Three gaps remain, all of them discovered by checking the code against the card rather than trusting the card:

- **A queued follow-up is invisible beyond a number.** The live area shows `2 queued` and nothing else, so a user who queued the wrong message cannot see what is waiting.
- **The word-movement and kill-word-forward intents are reachable only through Alt.** Termux and several terminals drop Alt on arrows and letters; with no second route those intents are simply unavailable there. The other Alt bindings are already covered: newline has `Ctrl+J`, the queue take-back has `Up`, and the model picker has `/model`.
- **`/help` prints two headings called `extensions`** when extension commands exist — the built-in group and the kind section — which reads as a duplicate rather than as two different things.

## What changes

1. The live-area footer shows the queue size **and the first queued line**, clipped to one line and never wrapped; the count stays even when the line is empty.
2. `Ctrl+Left` / `Ctrl+Right` route word-left / word-right, and `Ctrl+Delete` routes kill-word-forward, beside the existing Alt rows. The catalog stays the single table; no second routing path is introduced.
3. The `/help` kind section for extension commands is titled `extension commands`, so the built-in `extensions` group and the list of extension commands no longer share a heading.

## Impact

- `Lunate.Tui`: `LiveArea.SetQueued`, `LiveAreaState.QueuedFirst`, `LiveAreaRenderer`, `StatusFooterRenderer`, `KeyBinding` catalog.
- `Lunate.Coding`: `UpdateFollowUpIndicator` passes the first editor line.
- Help: `HelpView` heading table only.
- No public API, no session-format change, no new package.
