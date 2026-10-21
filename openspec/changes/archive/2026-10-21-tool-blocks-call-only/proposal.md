# Proposal — tool-blocks-call-only

## Why

Tool blocks committed to scrollback used to print their bounded excerpt and diff every time, so a
turn with many tool calls buried the conversation. PR #107 changed the default to a single header
line with a `ctrl+o` hint when more exists, and added the `toolDetails` setting; the `tui` and
`agent-config` specs still describe the old always-expanded form, so spec and code disagree.

## What Changes

- A finished tool block SHALL commit as its header line only by default.
- The header SHALL carry a muted `ctrl+o` hint only when expanded details would show something.
- `toolDetails: expanded` SHALL restore the bounded output excerpt and the diff panel.
- `toolDetails` SHALL default to `collapsed` and SHALL be overridable with `LUNATE_TOOL_DETAILS`.
- No code change: the implementation, tests and guide update already landed in PR #107.

## Capabilities

### New Capabilities

- None.

### Modified Capabilities

- `tui`: the "Tool block rendering" and "Diff rendering with match tier" requirements now describe
  the collapsed default, the `ctrl+o` hint and the expanded mode.
- `agent-config`: the "Settings file and precedence" requirement gains `toolDetails` and its
  environment override.

## Impact

- `openspec/changes/tool-blocks-call-only/` only: the two delta specs and their artifacts.
- No `src/` or `tests/` change, no new package, no new ADR.
- `docs/guide.md` already describes the shipped behaviour in PR #107.
