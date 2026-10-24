## Context

The session already records everything an export needs (`SessionEntry` hierarchy, tool calls with results, diffs, thinking blocks), and the terminal abstraction already knows whether it is interactive. What is missing is the two ends: a clipboard writer and a renderer.

Nothing here changes the session format, adds a package, or touches the extension surface.

## Decisions

1. **"The last answer" means the final assistant message of the most recent completed turn.** Not the whole transcript, not the last tool result. If the most recent turn ended in an error or a cancellation, the last completed answer before it. If there is none, `/copy` reports that plainly and copies nothing — an empty clipboard is worse than a notice, because the paste target looks successful.

2. **Clipboard transport depends on where the session runs, not on a fixed order.**
   - **Remote** (`SSH_CONNECTION` or `SSH_TTY` present): **OSC 52 only.** `pbcopy` there would write the *remote* clipboard, which is not the one the user is looking at — not a fallback, a wrong answer.
   - **Local**: the platform tool first, because its exit code proves the write; OSC 52 when no tool exists. **Windows: the Win32 clipboard API via P/Invoke, never `clip.exe`**, which mangles non-ASCII; **WSL**: `clip.exe` through interop, or PowerShell `Set-Clipboard`.
   - **Wording follows the evidence.** A platform tool is reported as `copied 1,842 characters via pbcopy`. OSC 52 cannot be confirmed, so it is reported as **`sent 1,842 characters to the terminal clipboard (OSC 52)`** — never "copied".
   - **tmux** (`TMUX` set): OSC 52 only works with `set-clipboard on`; when tmux is detected the notice says so once. **No DCS passthrough** — it is terminal-specific escaping for one multiplexer.

3. **OSC 52 payloads have a size limit and the notice says so.** The sequence is base64 of the UTF-8 answer. Some terminals cap it; above 100 KB the command reports that the copy **may be truncated** rather than silently sending a payload the terminal will drop, and never truncates the text itself.

4. **`/export jsonl` copies the session file byte for byte, minus redaction.** No re-serialisation: the format is the artifact, and a re-serialised copy would drift from the goldens the moment a field is added. Redaction (decision 6) is the only transformation.

5. **`/export html` renders from `SessionEntry`, in `Lunate.Coding`, with no Tui types.** One self-contained file, inline CSS, **no JavaScript**, no external assets, opens offline. Diffs are `<pre>` blocks with `+`/`-` classes; thinking blocks are `<details>` and collapsed by default; tool calls carry their name, arguments and result; every entry carries its timestamp. Redacted or signature-only thinking renders `[redacted thinking]`.

6. **Credential redaction is automatic, exact-value, and applies to both formats.** Every credential value Lunate knows — provider keys from the auth store and environment, extension secrets, MCP environment values — is replaced with `[redacted:<name>]`. **Exact-value replacement only**: no heuristic scanning, because a scan that guesses produces both false negatives and false positives and teaches users to trust a filter that cannot be trusted. A one-line notice after every export says the file may contain file contents and command output and should be reviewed before sharing. There is no confirmation gate.

7. **The HTML is safe to open with hostile content in it.** Everything taken from the session is escaped into **text nodes** only — never `innerHTML` built from content. No JavaScript is emitted (`<details>` covers collapsing), and the document carries `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src data:">`. A test feeds a tool result containing `<script>` and an `onerror` attribute and asserts both render as inert text.

8. **The destination is defaulted, never guessed.** `/export html [path]`; without a path the file is `~/.lunate/exports/<yyyy-MM-dd>-<session name or id>.<ext>`. **Not the current directory**: an export inside a repository ends up committed. A path argument overrides the default. An existing file is refused unless `--force` is passed. The confirmation always names the absolute path and the entry count.

9. **Both commands are interactive-only.** No ACP, no print mode: `/copy` needs a terminal to talk to, and `/export` in print mode would compete with the caller's own output contract. This matches the `/tree`-class commands.

10. **`Ctrl+X` is a catalog row, not a special case.** Same table the key-help overlay and P8's keybindings file read.

## Maintainer sign-off (2026-10-24)

- Transport split approved as written, with the OSC-52 wording, the tmux note, and the "may be truncated" limit.
- Default export path `~/.lunate/exports/<date>-<name>`, path argument overrides.
- No secret gate; automatic exact-value redaction plus the review notice, in both formats.
- Thinking included and collapsed; `[redacted thinking]` for redacted/signature-only.
- New security requirement: escaping, no JavaScript, CSP meta tag, and the inert-script test.

## Risks

- A second renderer drifts. Mitigated by rendering from `SessionEntry` only, and by a golden-file test on a fixture session that contains every entry kind.
- Clipboard helpers differ per distribution; the fallback chain is tested by faking the tool lookup, not by requiring the tools on CI. The Win32 path is compiled on Windows and exercised by the PowerShell leg.
- Redaction depends on knowing the value; a credential pasted into a prompt that Lunate never stored is not redacted. The review notice is the statement of that limit, not a substitute for it.
