# Design — tool-blocks-call-only

## Context

`ToolBlockRenderer` used to commit every finished call as a header, a bounded output excerpt and,
for `edit`/`write`, a red/green diff panel. The header carries the useful compact facts already
(glyph, tool name, path or command, line range or change counts, exit code and duration), so the
excerpt made a long turn noisy and pushed the conversation out of view. PR #107 collapses the
scrollback block to that header by default and leaves the full content to the existing `ctrl+o`
transcript viewer. The change is local to `Lunate.Tui`'s tool-block renderer and to
`SettingsStore`; no container boundary moves, so no C4 diagram is warranted.

## Decisions

### D1 — Collapsed is the default, expanded is the old look

The default `toolDetails` value is `collapsed`. A finished block then commits exactly one line:
the header. `toolDetails: expanded` keeps the previous block form — header, bounded output
excerpt, and the diff panel when the call produced one. The transcript viewer is unchanged and
always shows the full output, the diffs and the carried child result; collapsing the scrollback
loses no data, only the default visibility.

The setting is applied where the block is committed, not inside the renderer's header logic. The
renderer's `ToolDetails` argument (and the no-detail overload, which defaults to collapsed) is the
only switch; the transcript viewer asks for the full form independently.

### D2 — Header formats

The header itself is unchanged; the setting changes only what follows it. Full-width forms
(≥60 columns) are:

| Tool | Collapsed header | Hint |
| --- | --- | --- |
| `read` (ok) | `✓ read  src/a.cs (13 lines)` | no |
| `read` (error) | `✗ read  src/missing.cs · ctrl+o` | yes |
| `bash` | `✓ bash  dotnet build · exit 0 · 0.4s · ctrl+o` | yes, when output exists |
| `edit` | `✓ edit  src/x.cs · +1 −1 · ctrl+o` | yes, when the diff is non-empty |
| `write` | `✓ write  src/NewFile.cs · +3 −0 · ctrl+o` | yes, when the diff is non-empty |
| `cs_*` | `✗ cs_diagnostics  changed · 2 errors 1 warning · src/a.cs(4,5): error CS0103: Th… · ctrl+o` | yes, when output exists |
| extension/MCP | `✓ hello-tool  ~/dev/lunate/src/L…/ToolBlockRenderer.cs` | yes, when output exists |

Below 60 columns the existing short form still applies: glyph, name and meta only
(`✓ read · 13 lines`, `✓ bash · exit 0 · 0.4s`, `✓ edit · +5 −1`), with the same hint rule.
The hint uses the header's separator — ` · ` in Unicode, ` - ` in ASCII — and the muted style;
the ASCII glyphs stay `+` and `x`, and the excerpt marker's `…` becomes `...`.

### D3 — The hint rule

A collapsed header carries `ctrl+o` only when expanded details would show at least one line:

- a non-blank output excerpt for a block whose output the scrollback shows (`bash`, `cs_*`,
  extension/MCP tools, and a failed `read` whose message is the error), or
- a diff that parses into at least one panel line for `edit`/`write`.

It is omitted when there is nothing more to show: blank output, an empty or malformed diff, or a
successful `read`, whose output the scrollback never shows. Expanded blocks carry no hint because
the details are already visible; the viewer's full form never carries one either. The hint is
computed from the same excerpt and diff builders the block would render, so it cannot advertise
an empty detail.

### D4 — `toolDetails` setting

`~/.lunate/settings.json` takes the optional `toolDetails` key with the exact lowercase literals
`collapsed` (default) and `expanded`. `LUNATE_TOOL_DETAILS` overrides the file value; an empty
environment value is ignored like the other settings. A rejected value in either place joins the
one collected settings error, names the accepted literals and applies no partial settings. The
resolved value lives on `AgentSettings.ToolDetails` and is passed to the renderer at commit time.

## Risks / Trade-offs

- [Users no longer see output by default] → the `ctrl+o` hint marks exactly the blocks with more,
  the existing viewer shows it, and `toolDetails: expanded` restores the old look.
- [A hint on every block would be noise] → the hint is conditional on expanded details; a
  successful `read` and a blank output stay clean.
- [Hint logic could drift from the rendered details] → it calls the same excerpt and diff builders;
  renderer tests pin the read, blank-output, diff and expanded cases.
- [The setting adds another validation path] → file and environment share one parser and one
  error message, and invalid values are collected with the other problems.

## Open Questions

- None. The default, the hint rule and the expanded form are settled by PR #107.
