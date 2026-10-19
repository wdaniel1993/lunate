# Tasks: tool blocks — compact headers and a transcript viewer

## 1. Summaries and meta

- [x] 1.1 `read` summary: the path with its line range (`path (start-end)` from the call's
  `offset`/`limit`; `path (N lines)` from the result's line count when the call gave none).
- [x] 1.2 `bash`: `ToolResult` gains the `BashDetails(ExitCode)` payload; the header meta carries
  `exit N · <duration>`.
- [x] 1.3 `edit`/`write`: meta carries `+N −M` from the existing diff info, before the diff panel.
- [x] 1.4 `cs_*`: meta carries the severity counts and the top items (from the result's summary
  line, capped).
- [x] 1.5 Extension/MCP tools: the generic fallback stays, with the path-shortening applied where
  the value looks like a path.

## 2. Header, glyphs, excerpts

- [x] 2.1 The header becomes `glyph name  summary · meta`: `✓`/`✗` (ASCII `+`/`x` per the
  Unicode capability), the name in accent, summary and meta muted; the status words go.
- [x] 2.2 Duration measured on the injected scheduler from `ToolCallStart` to `ToolCallResult`
  (culture-invariant `0.4s` / `12s` / `1m 02s`; one decimal below 10 s).
- [x] 2.3 `OutputExcerpt` gains the tail-only mode for `bash` (5 lines + marker); `read` gets no
  excerpt; everything else keeps first-and-last.
- [x] 2.4 The marker becomes `… +N lines · ctrl+o` (warning colour; ASCII `... +N lines -
  ctrl+o`).

## 3. Path shortening

- [x] 3.1 The pure shortening helper (`~` for home; middle elision keeping the basename; never
  wraps) and its unit tests; applied in summaries and the viewer.

## 4. Transcript store and viewer

- [x] 4.1 The session's in-memory transcript store (user/assistant text, thinking, blocks with
  full output and diff; commit order; nothing new persisted).
- [x] 4.2 The viewer renderer: full-screen layout, newest at the bottom, opens at the latest,
  full outputs/diffs, mid-shortened paths, a footer line with keys and position.
- [x] 4.3 The alternate-screen lifecycle through `IConsoleIO` (enter/leave; restored on every
  exit path including exceptions).
- [x] 4.4 The `Ctrl+O` binding through the key pump (refused while an approval or picker is open;
  no input reaches the model while open); the scrollback stays unchanged on close; the footer
  keybinding line names it.
- [ ] 4.5 Navigation: Up/Down, PageUp/PageDown, Home/End, `g`/`G`; `q`/`Esc` closes.

## 5. Subagent grouping

- [x] 5.1 The spawning block's carried child result renders as one indented group (child stays
  quiet in scrollback, per the existing scenario).

## 6. Tests and media

- [ ] 6.1 Snapshot goldens for the block forms (read/bash/edit+diff/cs_*/fallback; running is the
  live area, unchanged) at 120/80/50 × truecolor/256/16/none.
- [ ] 6.2 Snapshot goldens for the viewer at the same breakpoints × colour modes.
- [ ] 6.3 The production composition test: the real host renders a scripted session with tool
  calls → compact blocks; `Ctrl+O` opens the viewer with full content; close restores.
- [ ] 6.4 The VHS tape (`features-tools`) driven by the replay client + the GIF and the README
  gallery entry.
- [ ] 6.5 Startup budget unchanged (the viewer is lazy); verify via the change gate.

## 7. Docs

- [ ] 7.1 `docs/guide.md`: the tool-block forms and the `Ctrl+O` viewer in the TUI section.
- [ ] 7.2 The footer keybinding line change noted where keybindings are documented.
