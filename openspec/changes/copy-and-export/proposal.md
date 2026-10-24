# Copy and export

## Why

Two things a terminal agent is expected to do and Lunate cannot:

- **Get an answer out of the terminal.** Today the only way to copy the last answer is to select it with the mouse, which fails over ssh, in Termux, and on any wrapped line. There is no `/copy` and there is no clipboard path at all.
- **Keep a session.** Session files are JSONL by design, but nothing turns them into something a human reads afterwards — no `/export`, no HTML, no portable artifact to attach to an issue or a review.

## What changes

- **`/copy` and `Ctrl+X`** copy the **last assistant answer** — the final answer of the most recent completed turn, not the whole transcript — to the system clipboard. The write goes through **OSC 52** first, which works over ssh and in Termux without any helper process, then falls back to the platform tool (`pbcopy` on macOS, `wl-copy`/`xclip` on Linux, `clip` on Windows) when the terminal does not acknowledge.
- **`/export html|jsonl`** writes the session:
  - `jsonl` copies the session file verbatim under a new name — the format is already the artifact.
  - `html` writes one self-contained file with no external assets: messages, tool calls and their results, diffs, thinking blocks, and the model/turn boundaries. It opens from disk with no network.

Neither is a new session-format feature: both read what is already on disk.

## Devil's advocate

- **OSC 52 is a terminal capability, not a guarantee.** Some terminals ignore it silently; some proxies strip it. That is why the platform tool is a fallback rather than the other way round: the fallback proves the copy happened, and `/copy` reports which path it used, so "nothing happened" never has to be guessed.
- **`Ctrl+X` collides with nothing in the current catalog, but it is not a universal reflex** either. `/copy` is the discoverable route and appears in help; the key is the shortcut for people who want one. Termux-safe by construction: it is a Ctrl chord, not an Alt one.
- **HTML is a second renderer, and every second renderer drifts.** The mitigation is that it renders from the same `SessionEntry` model the transcript does and lives in `Lunate.Coding`, not in `Lunate.Tui` — no terminal concepts, no ANSI, no width arithmetic. If it ever needs the TUI to render a block, that is a signal the block belongs in the model.
- **Exporting a session exports secrets.** Tool results can contain tokens, and thinking blocks can contain anything the model was shown. `/export` therefore writes with the same permissions as the session file and says in its confirmation how many entries it wrote and where — never a silent file in the current directory.
- **Rejected: `/share`.** Uploading to a third party is a product decision with a hosting story, not a rendering feature; it is not in this change.

## Impact

- `Lunate.Coding`: `/copy` command, clipboard writer (OSC 52 + platform fallbacks), `/export` command, HTML writer.
- `Lunate.Tui`: one catalog row for `Ctrl+X`; no renderer changes.
- Specs: `tui` gains both commands and the binding; no new capability, because both are surface over an existing session.
- No package, no public API, no session-format change.
