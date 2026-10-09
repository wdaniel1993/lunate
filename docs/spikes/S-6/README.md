# S-6 spike — XenoAtom.Terminal.UI inline mode

Throwaway evaluation of [XenoAtom.Terminal.UI](https://github.com/XenoAtom/XenoAtom.Terminal.UI)
3.10.0 (inline `Terminal.Live` + `PromptEditor` + Markdig `MarkdownControl`)
as a replacement for Lunate's own live area and input line, keeping
scrollback-first inline output. Findings live in [`report.md`](report.md) and
[`../../../adr/0019-xenoatom-terminal-ui.md`](../../../adr/0019-xenoatom-terminal-ui.md).

- `S6.Harness/` — the S-5 scenario rebuilt on XenoAtom.Terminal.UI
  (`Terminal.Write` finished blocks, `Terminal.Live` live area, `PromptEditor`
  input/steering, Markdown via the Markdig extension package); modes
  `--scenario`, `--startup`, `--idle`, `--burst`, `--interactive`.
- `S6.Tests/` — headless tests: deterministic frames via the reflected internal
  `TerminalApp.Tick` hook, and live-loop tests through
  `InMemoryTerminalBackend` (scriptable input, captured output).
- `scripts/` — flake run, scenario replay, startup/memory/dependency
  measurements, LOC.
- `evidence/` — raw measurements and research notes.

Not in `lunate.sln`, not part of `scripts/verify.sh`; `src/` and `tests/` are
untouched. The S-5 baseline project under `docs/spikes/S-5` is used read-only
as the comparison build.
