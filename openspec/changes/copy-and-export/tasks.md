## 1. Copy

- [ ] 1.1 Red: pick the last completed answer (skipping a failed or cancelled tail); nothing to copy when no turn completed.
- [ ] 1.2 Red: transport selection — ssh present means OSC 52 only and no child process; local means the platform tool first; no tool means OSC 52.
- [ ] 1.3 Red: the 100 KB refusal names the size and writes nothing.
- [ ] 1.4 `ClipboardWriter` with the OSC 52 encoder, the platform-tool table and the transport report.
- [ ] 1.5 `/copy` command, notice text, and the `Ctrl+X` catalog row.
- [ ] 1.6 Help row and guide row; the overlay picks the row up from the catalog.

## 2. Export

- [ ] 2.1 Red: `jsonl` is byte-identical to the session file.
- [ ] 2.2 Red: `html` contains the answer, the diff and the thinking text, references no external URL, and escapes a hostile tool result.
- [ ] 2.3 Red: an existing destination is refused without `--force`.
- [ ] 2.4 `SessionHtmlWriter` in `Lunate.Coding`: one escaping helper, inline CSS, `<details>` for thinking, diff classes, timestamps.
- [ ] 2.5 `/export` command with path defaulting, `--force`, and the confirmation naming path and entry count.
- [ ] 2.6 Golden-file test on a fixture session containing every entry kind.

## 3. Wiring and gate

- [ ] 3.1 Production composition test: the real host wires both commands and the binding.
- [ ] 3.2 Interactive-only: assert neither command resolves in ACP or print mode.
- [ ] 3.3 Docs: `README.md` and `docs/guide.md` rows, `docs/spec/` page for the export contract.
- [ ] 3.4 `openspec validate copy-and-export --strict`; csharpier; Release build; focused suites; full `scripts/verify.sh`; `ci-ok`; archive as the final commit of the same PR.
