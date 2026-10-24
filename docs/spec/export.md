# Export contract

`/export` turns a session file into something a person can read, attach to an issue or hand to a
reviewer. This page owns the rules that make that artifact safe and predictable: the destination,
the credential redaction, the HTML document's guarantees, and where the commands exist. The
behaviour itself is specified in [`openspec/specs/tui/spec.md`](../../openspec/specs/tui/spec.md);
`docs/guide.md` owns the user-facing walkthrough.

## Forms

| Form | What is written |
| --- | --- |
| `/export jsonl [path]` | The session file byte for byte, with only the credential redaction applied. No re-serialization: the format is the artifact, and a re-serialized copy would drift from the golden fixtures the moment a field is added. |
| `/export html [path]` | One self-contained HTML document built from the session's `SessionEntry` records: messages, tool calls with their arguments and results, diffs, thinking, compaction and model changes, each with its timestamp. |

Both forms read what is already on disk. Neither changes the session file.

## Destination

- Without a path the file is `~/.lunate/exports/<yyyy-MM-dd>-<session name or id>.<ext>`, with the
  local date and the `/name` title when one is set. The current directory is never used: an export
  written inside a repository ends up committed.
- A path argument overrides the default; a relative path resolves against the session's working
  directory.
- An existing destination is refused unless `--force` is passed.
- The destination is always printed as an absolute path, with the number of entries written, on
  the line after the write. The exporter refuses to write over the session file itself.
- The exported file inherits the session file's permissions on POSIX, so it is never more readable
  than the session it came from.

## Credential redaction

Every credential value Lunate itself holds is replaced with `[redacted:<name>]` in **both** forms,
before anything is written:

| Source | Name |
| --- | --- |
| `~/.lunate/auth.json` keys | the key's name, e.g. `openai` |
| Provider environment variables and their aliases | the variable name, e.g. `ANTHROPIC_API_KEY` |
| `<store>/extensions-secrets/<id>.json` values | `<extension id>.<secret name>` |
| Each configured MCP server's `env` values | `mcp.<server>.<variable>` |

Replacement is by **exact value only**. No pattern that "looks like a key" is touched, because a
guess produces both false negatives and false positives and teaches the reader to trust a filter
that cannot be trusted. Values shorter than four characters are skipped: replacing them would
rewrite the document without protecting anything. A credential that was never stored — one pasted
into a prompt — is not redacted, which is why every export ends with the review notice:

```text
may contain file contents and command output; review before sharing
```

There is no confirmation gate; the notice states the limit of the filter rather than asking a
question the file has already answered.

## The HTML document

The document is safe to open even when a tool result carries hostile repository content:

- **No JavaScript.** Nothing executable is emitted; collapsing is `<details>`, not a script.
- **One escaping helper.** Every value taken from the session is escaped into a text node. Content
  never becomes markup, so a tool result containing `<script>` or `<img onerror=...>` renders as
  those characters.
- **No external asset.** Inline CSS only; nothing is fetched when the file is opened offline.
- **A Content-Security-Policy meta tag** with exactly
  `default-src 'none'; style-src 'unsafe-inline'; img-src data:`.
- **Thinking included, collapsed by default** in a `<details>` element. Thinking the provider
  redacted or returned only a signature for renders `[redacted thinking]`.
- **Diffs classed.** Lines of a tool result that begin with `+` or `-` (and are not the `+++`/`---`
  header) are wrapped in `diff-add`/`diff-del` spans inside a `<pre>` block. Diffs live in the
  tool result text, which is what the session records.

The test suite feeds a tool result containing `<script>alert(1)</script>` and
`<img src=x onerror=alert(1)>` and asserts both appear as escaped text, with no `<script` element
and no event-handler attribute in the document.

## Where the commands exist

`/copy` and `/export` are interactive-only. They are registered by the interactive session's
built-in command source, and print mode and ACP compose no command registry at all — the same text
is passed to the model as a prompt. `/copy` needs a terminal to talk to; `/export` in print mode
would compete with the caller's own output contract.

## Copy transport

`/copy` (and `Ctrl+X`) writes the final assistant message of the most recent completed turn. A
turn that ended in an error or a cancellation is a failed tail, so the previous completed answer is
used; when no turn has completed, the command says so and writes nothing.

- Over ssh (`SSH_CONNECTION` or `SSH_TTY` set) only the OSC 52 sequence is written. A local tool
  there would write a clipboard the user is not looking at.
- Locally the platform tool is tried first, because its exit code proves the write: `pbcopy` on
  macOS, `wl-copy`/`xclip`/`xsel` on Linux, `clip.exe` or PowerShell's `Set-Clipboard` under WSL,
  and the Win32 clipboard API through P/Invoke on Windows — never `clip.exe`, which mangles
  non-ASCII text. OSC 52 is the fallback when no tool exists or the tool fails.
- The notice states what was proven: `copied 1,842 characters via pbcopy` for a tool, `sent 1,842
  characters to the terminal clipboard (OSC 52)` for the sequence. Under tmux (`TMUX` set) the
  notice adds that OSC 52 needs `set-clipboard on`; above 100 KB it adds that the terminal may
  truncate the copy. The text itself is never truncated by Lunate.
