# Copy and export

## Why

Two things a terminal agent is expected to do and Lunate cannot:

- **Get an answer out of the terminal.** Today the only way to copy the last answer is to select it with the mouse, which fails over ssh, in Termux, and on any wrapped line. There is no `/copy` and there is no clipboard path at all.
- **Keep a session.** Session files are JSONL by design, but nothing turns them into something a human reads afterwards — no `/export`, no HTML, no portable artifact to attach to an issue or a review.

## What changes

- **`/copy` and `Ctrl+X`** copy the **last assistant answer** — the final answer of the most recent completed turn, not the whole transcript.
  - **Over ssh** (`SSH_TTY` or `SSH_CONNECTION`): **OSC 52 only** — a local `pbcopy` there would write the wrong clipboard.
  - **Locally**: the **platform tool first** (its exit code proves the write), OSC 52 as the fallback. Windows uses the **Win32 clipboard API via P/Invoke**, never `clip.exe`, which mangles non-ASCII; WSL uses `clip.exe` through interop or PowerShell `Set-Clipboard`.
  - OSC 52 cannot confirm the write, so the notice says **"sent to the terminal clipboard (OSC 52)"**, never "copied". Under **tmux** (`TMUX` set) OSC 52 requires `set-clipboard on`, and the notice says so once. Terminals cap OSC 52 payloads; above the cap the notice says the copy **may be truncated**.
- **`/export html|jsonl`** writes the session:
  - `jsonl` copies the session file **byte for byte**, minus the credential redaction below.
  - `html` writes **one self-contained document** with no external assets, no JavaScript, and a CSP of `default-src 'none'; style-src 'unsafe-inline'; img-src data:` — safe to open even when a tool result contains hostile markup.
  - Default destination is `~/.lunate/exports/<yyyy-MM-dd>-<session name or id>.<ext>`, **not the current directory**: an export inside a repo ends up committed. A path argument overrides it. The full path is always printed.
- **Credential values Lunate knows are redacted automatically** in both formats — provider keys from the auth store and environment, extension secrets, MCP environment values — replaced with `[redacted:<name>]`. **Exact-value replacement only, no heuristic scanning.** A one-line notice after export says the file may contain file contents and command output and should be reviewed before sharing.

Neither is a new session-format feature: both read what is already on disk.

## Devil's advocate

- **OSC 52 is a terminal capability, not a guarantee.** Some terminals ignore it and some proxies strip it. That is why it is the only path over ssh — the alternative writes a clipboard the user cannot see — and why the wording changes from "copied" to "sent", so the user is never told something that was not verified.
- **`Ctrl+X` collides with nothing in the current catalog, but it is not a universal reflex** either. `/copy` is the discoverable route and appears in help; the key is the shortcut for people who want one. Termux-safe by construction: it is a Ctrl chord, not an Alt one.
- **Redaction by exact value is narrow by design.** A heuristic scan for "looks like a key" produces false negatives and false positives, and teaches users to trust a filter that cannot be trusted. What Lunate knows, it removes; what it does not know, the review notice covers.
- **HTML is a second renderer, and every second renderer drifts.** The mitigation is that it renders from the same `SessionEntry` model the transcript does and lives in `Lunate.Coding`, not in `Lunate.Tui` — no terminal concepts, no ANSI, no width arithmetic. If it ever needs the TUI to render a block, that is a signal the block belongs in the model.
- **Exporting a session exports secrets.** Tool results can contain tokens, and thinking blocks can contain anything the model was shown. Redaction is automatic rather than opt-in, the file inherits the session's permissions, and the review notice is unconditional.
- **Rejected: `/share`.** Uploading is a product decision with a hosting story, not a rendering feature.
- **Rejected: DCS passthrough for tmux.** It is a pile of terminal-specific escaping for one multiplexer; the notice tells the user the one setting that makes OSC 52 work.

## Impact

- `Lunate.Coding`: `/copy` command, clipboard writer (OSC 52 + platform tools + Win32 P/Invoke), `/export` command, HTML writer, credential redactor.
- `Lunate.Tui`: one catalog row for `Ctrl+X`; no renderer changes.
- Specs: `tui` gains both commands and the binding; a new `export` capability carries the HTML contract, the redaction rule and the CSP.
- No package, no public API, no session-format change.
