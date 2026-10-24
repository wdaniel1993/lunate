## Context

The session already records everything an export needs (`SessionEntry` hierarchy, tool calls with results, diffs, thinking blocks), and the terminal abstraction already knows whether it is interactive. What is missing is the two ends: a clipboard writer and a renderer.

Nothing here changes the session format, adds a package, or touches the extension surface.

## Decisions

1. **"The last answer" means the final assistant message of the most recent completed turn.** Not the whole transcript, not the last tool result. If the most recent turn ended in an error or a cancellation, the last completed answer before it. If there is none, `/copy` reports that plainly and copies nothing — an empty clipboard is worse than a notice, because the paste target looks successful.

2. **Clipboard transport depends on where the session runs, not on a fixed order.**
   - **Remote** (`SSH_CONNECTION` or `SSH_TTY` present): OSC 52 only. `pbcopy` there would write the *remote* clipboard, which is not the one the user is looking at, so it is not a fallback — it is a wrong answer.
   - **Local**: the platform tool first (`pbcopy`, `wl-copy`, `xclip -selection clipboard`, `clip`), because its exit code proves the write; OSC 52 if no tool exists.
   - `/copy` names the path it used (`copied 1,842 characters via OSC 52` / `via pbcopy`), because a silent failure and a silent success look identical otherwise.

3. **OSC 52 is written to the terminal, never to a file descriptor we do not own.** The sequence is base64 of the UTF-8 answer, unbounded by our own limit but bounded by what the terminal accepts; a payload over 100 KB is refused with a notice rather than truncated, because a truncated answer pasted as if complete is a silent corruption.

4. **`/export jsonl` copies the session file byte for byte.** No re-serialisation: the format is the artifact, and a re-serialised copy would drift from the goldens the moment a field is added.

5. **`/export html` renders from `SessionEntry`, in `Lunate.Coding`, with no Tui types.** One self-contained file, inline CSS, no JavaScript, no external assets, opens offline. Diffs are `<pre>` blocks with `+`/`-` classes; thinking blocks are `<details>`; tool calls carry their name, arguments and result; every entry carries its timestamp. Escaping is centralised in one helper — an export that mangles a diff or executes a tool result is worse than no export.

6. **The destination is explicit or defaulted, never guessed.** `/export html [path]`; without a path the file is `<session-id>.html` in the current directory. An existing file is refused unless `--force` is passed. The confirmation names the absolute path and the entry count.

7. **Both commands are interactive-only.** No ACP, no print mode: `/copy` needs a terminal to talk to, and `/export` in print mode would compete with the caller's own output contract. This matches the `/tree`-class commands.

8. **`Ctrl+X` is a catalog row, not a special case.** Same table the key-help overlay and P8's keybindings file read.

## Open questions for the maintainer

- **Q1** — Remote uses OSC 52 only, local prefers the platform tool. The alternative is OSC 52 everywhere (simpler, but locally it can fail silently where `pbcopy` would have succeeded). Recommendation: **as written**.
- **Q2** — Default export path: current directory (`./<session-id>.html`) versus `~/.lunate/exports/`. Recommendation: **current directory**, because the file is meant to be attached or opened immediately, and a hidden directory makes it a hunt.
- **Q3** — Should `/export` refuse when the session contains tool results from a trusted-but-secret-bearing command? Recommendation: **no special casing** — the confirmation states the entry count and path, the file inherits the session's permissions, and the user asked for it.
- **Q4** — HTML fidelity: include thinking blocks by default, or behind `--thinking`? Recommendation: **include by default in a collapsed `<details>`**, since the session file already contains them and hiding them makes the export a different document than the session.

## Risks

- A second renderer drifts. Mitigated by rendering from `SessionEntry` only, and by a golden-file test on a fixture session that contains every entry kind.
- Clipboard helpers differ per distribution; the fallback chain is tested by faking the tool lookup, not by requiring the tools on CI.
