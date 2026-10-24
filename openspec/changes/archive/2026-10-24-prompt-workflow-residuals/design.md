## Context

The pi-parity epic closed P5 by shipping bash mode, the follow-up queue and `/compact` instructions. This change does not re-open any of that; it closes three gaps that the shipped behaviour leaves, each verified in the code before it was written down here.

The queue is bounded at five entries and drained in submission order. The footer already receives the count, which means the transport exists; only the payload is missing.

The binding catalog is the single routing table (`KeyBinding`), and every row already carries a description and a context. Adding a row is the whole mechanism — the reason to do it here rather than in P8 is that a binding with no Termux route is not a cosmetic gap: it is an intent that cannot be reached on the platform the maintainer actually uses over ssh.

## Decisions

1. **The indicator carries the first queued line, not a summary of the queue.** The footer is one line and must not wrap; the first line of the head entry is the piece a user can recognise an accidental queue by. Clipping happens in the renderer with the same cell-width rules as the rest of the footer, and the count is rendered even when the line clips to nothing.
2. **Termux-safe alternatives are Ctrl-based and route the same intent.** `Ctrl+Left`/`Ctrl+Right` for word movement and `Ctrl+Delete` for kill-word-forward. Alt rows stay, because nothing is removed for users whose terminals deliver Alt correctly; the catalog simply stops being the only route.
3. **No alternatives are invented for bindings that already have one.** `Ctrl+J` already inserts a newline everywhere, `Up` with a non-empty queue already takes the queue back, and `/model` already opens the picker — adding a second key for those would grow the table without adding a route.
4. **The help kind section is renamed, the built-in group is not.** `extensions` is the group of commands that inspect extensions; `extension commands` is what extensions offer. Renaming the group would misdescribe built-ins; renaming the section costs one word and removes the collision.

## Risks

- `Ctrl+Left`/`Ctrl+Right` are not universally decoded; a terminal that reports them as plain arrows loses nothing (the previous behaviour), and one that reports them as `Escape` sequences still routes through the existing decoder. The rows are additive, so the failure mode is "as before", never "a key that used to work stopped".
- `Ctrl+Delete` is the least portable of the three. It is documented as terminal-dependent in the help row, and it is an addition, not a replacement.

## Out of scope

The external editor (`Ctrl+G`) stays in `t_7a383140`. No multi-line queue preview, no queue reordering, no persistence.
