# Design: tool blocks — compact headers and a transcript viewer

## D1. The block header

One line: `glyph name  summary · meta`.

- Glyph: `✓` (ok, success colour), `✗` (failed, error colour). ASCII fallback per the terminal's
  Unicode capability: `+` and `x`. The running state keeps its existing home — the live area's
  active tool line with its spinner; blocks only ever commit finished.
- Name: the tool name in the accent colour.
- Summary: the per-tool compact form (D2), muted.
- Meta: tool-specific facts joined with `·` (D2), muted.

The existing "tool <name> … <status-word>" header is replaced; `ToolBlockRenderer` keeps its
borderless, markup-escaped style and its `IRenderable` contract.

## D2. Per-tool summaries and meta

`ToolArgsSummary` grows into the compact form; the meta comes from the call's result and timing:

| Tool | Summary | Meta |
| --- | --- | --- |
| `read` | the path with its line range (`TodoList.cs (1-13)`; the range from the call's `offset`/`limit` when present, else the result's line count) | — |
| `bash` | the command (existing 60-char cap) | `exit N` · duration |
| `edit`, `write` | the path | `+N −M` change counts, then the existing diff panel |
| `cs_*` | the symbol/file | severity counts (`E errors W warnings`) + the top items from the result |
| extension/MCP tools | the tool name | the existing first-string fallback, shortened |

Data sources, all existing or additive:

- `bash` exit code: `ToolResult` for bash gains a `BashDetails(int ExitCode)` payload (the
  `Details` object already exists and already carries the edit/write diff info; the footer text
  stays for the model).
- Duration: measured in the wiring from `ToolCallStart` to `ToolCallResult` on the injected
  `IScheduler` clock — no wall-clock in renderers, deterministic under `TestScheduler`. Format:
  `0.4s`, `12s`, `1m 02s` (culture-invariant; one decimal below 10 s).
- Change counts: from the existing diff info (added/removed lines of the unified diff).
- `cs_*` counts: the tool result's own summary line; the top items are the first few diagnostic
  lines, capped.

Unknown tools keep the generic fallback (first string value, then raw text).

## D3. Output excerpts per tool

- `read`: no output excerpt at all (the block identifies what was read; the content lives in the
  transcript viewer).
- `bash`: the output's last lines (5, then the elision marker) — build logs end with the verdict.
- everything else: the existing first-and-last-lines excerpt (8 + 8).
- The elision marker becomes `… +N lines · ctrl+o` in the warning colour (ASCII: `... +N lines -
  ctrl+o`), where N is the hidden-line count.

`OutputExcerpt` grows a mode parameter (head+tail default, tail-only for bash); the marker text
moves into the excerpt model so the renderer stays dumb.

## D4. The transcript store

The session keeps a bounded in-memory transcript for the viewer: per committed item — user text,
assistant text, thinking (with its collapsed/full text), tool blocks (name, summary, full output,
diff) — appended in commit order. It is a view over data the session already holds (history,
block models); nothing new is persisted, and the session file stays exactly as it is. The store is
capped by the session's own lifetime, not by a separate budget.

## D5. The transcript viewer

- Binding: `Ctrl+O` (no approval prompt or picker open), and `Ctrl+O` again or `q`/`Esc` closes.
- Surface: the alternate screen (`\u001b[?1049h` … `\u001b[?1049l`) through the `IConsoleIO` seam,
  restored on every exit path (including exceptions). This is the TUI's first full-screen surface;
  it is a rendering mode of the existing architecture (same seam, same key pump), not a new
  architecture — no ADR is raised, and the design notes it here deliberately.
- Layout: full width, newest entry at the bottom; the view opens scrolled to the latest content.
- Navigation: Up/Down (line), PageUp/PageDown, Home/End, `g`/`G`; the footer line shows the keys
  and the position (`42%`).
- Rendering: the same renderers as the scrollback where possible (markdown, thinking, blocks with
  full output and diffs); paths mid-shortened (D6); nothing wraps past the width.
- While open, no input reaches the model; the session's key pump routes keys to the viewer.
- The footer's keybinding line gains `ctrl+o transcript`.

## D6. Path shortening

A pure helper: home becomes `~`, then the middle of the path elides (`~/dev/l…/Session.cs`) so the
basename survives and the result fits the budget; never wraps. Used by summaries and the viewer.

## D7. Subagents

The existing contract stands: child events never render in scrollback; the live area shows
`subagent running`; the spawning block carries the child's final result; child usage feeds the
footer. The refinement here: the carried result renders as one indented group under the spawning
block's header, so a subagent reads as a single unit, not interleaved lines.

## D8. Determinism

Every new surface is driven by the injected scheduler and the event stream: durations, the
viewer's position, the glyphs. Snapshot goldens pin frames per breakpoint (120/80/50) and colour
mode (truecolor/256/16/none); the composition test drives the real host through a scripted
fixture; the VHS tape renders from the replay client; the startup budget stays unchanged (the
viewer is lazy — nothing renders until opened).

## D9. Layering and packages

No new packages. The TUI stays free of `Lunate.Coding` references (the block model stays
primitives; the wiring maps details). The `IConsoleIO` seam gains only what the alternate screen
and full-screen paint need; platform detection stays where it is.

## D10. What stays

Scrollback-first stays (ADR-0007/0019): blocks commit once, the viewer is on demand. The live
area's active tool line, the approval prompt, the diff rendering with match tiers, and the
child-run contract are untouched except where this design says otherwise.
