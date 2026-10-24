## 1. Copy

- [ ] 1.1 Red: pick the last completed answer (skipping a failed or cancelled tail); nothing to copy when no turn completed.
- [ ] 1.2 Red: transport selection — ssh present means OSC 52 only and no child process; local means the platform tool first; no tool means OSC 52; `TMUX` adds the `set-clipboard on` line once.
- [ ] 1.3 Red: the notice distinguishes "copied … via <tool>" from "sent to the terminal clipboard (OSC 52)"; above 100 KB it says the terminal may truncate.
- [ ] 1.4 `ClipboardWriter`: OSC 52 encoder, platform-tool table, **Win32 clipboard via P/Invoke on Windows** (never `clip.exe`), WSL via `clip.exe`/`Set-Clipboard`.
- [ ] 1.5 `/copy` command, notice text, and the `Ctrl+X` catalog row.
- [ ] 1.6 Help row and guide row; the overlay picks the row up from the catalog.

## 2. Export

- [ ] 2.1 Red: `jsonl` preserves every entry byte-identically, with the credential redaction applied.
- [ ] 2.2 Red: the default destination is `~/.lunate/exports/<yyyy-MM-dd>-<session name or id>.<ext>`; a path argument overrides it; the absolute path is printed.
- [ ] 2.3 Red: `html` contains the answer, the diff, the collapsed thinking block, the CSP meta tag, no script element and no external URL.
- [ ] 2.4 Red: `<script>` and `onerror` in a tool result render as escaped text.
- [ ] 2.5 Red: an existing destination is refused without `--force`.
- [ ] 2.6 `SessionHtmlWriter` in `Lunate.Coding`: one escaping helper, text nodes only, inline CSS, `<details>` for thinking, diff classes, timestamps.
- [ ] 2.7 `CredentialRedactor`: exact-value replacement from the auth store, environment, extension secrets and MCP env values, naming each as `[redacted:<name>]`; wired into both formats.
- [ ] 2.8 `/export` command with path defaulting, `--force`, the confirmation and the review notice.

## 3. Wiring and gate

- [ ] 3.1 Production composition test: the real host wires both commands and the binding.
- [ ] 3.2 Interactive-only: assert neither command resolves in ACP or print mode.
- [ ] 3.3 Docs: `README.md` and `docs/guide.md` rows, `docs/spec/` page for the export contract (CSP, redaction, destination).
- [ ] 3.4 `openspec validate copy-and-export --strict`; csharpier; Release build; focused suites; full `scripts/verify.sh`; `ci-ok`; archive as the final commit of the same PR.
