# Proposal: Tool blocks and diff rendering (T-20)

## Why

The transcript is where a coding agent proves itself: every tool call becomes a scrollback block with name, argument summary, status and a bounded view of its output — and for `edit`/`write`, a red/green diff panel that shows exactly what changed, with the match tier visible (the guide's "every fallback is visible" rule: an `edit` that succeeded via a normalized match must say so). The data is already on the wire: T-13's `LineDiff` produces Myers-based unified diffs, `EditDetails` (path, line range, match tier, diff) travels UI-only through `ToolCallResult.Details`, and `LineDiff`'s unified format is the rendering contract. T-20 turns that into the `ToolBlock` component T-19's renderer set lacks, snapshot-pinned like everything else in the TUI.

## What changes

- **`ToolBlockModel`** (public, `Lunate.Tui`): tool name, argument summary, status (running/ok/error), output text, optional diff info (path, match tier, unified diff) — primitives only, so the TUI never references `Lunate.Coding`; T-22 maps `EditDetails` to it.
- **`ToolBlockRenderer.Render(ToolBlockModel) -> IRenderable`**: header line (tool name + argument summary + status), output shown as first and last lines with an elision marker for the middle, borderless like T-19's code fences.
- **Argument summaries**: a small per-tool summarizer (`read`/`write`/`edit` → path, `bash` → command, `cs_*` → symbol/name, generic fallback → first meaningful JSON line) — pure, unit-tested.
- **Diff panel**: parses the unified diff (headers, hunks, +/-/context) into a red/green borderless panel with a `match: exact|normalized` label — normalized matches visibly flagged; escaping discipline unchanged.
- **Goldens incl. match tier**: ok/error blocks, long-output truncation, bash, diff panels for both tiers, write block — `TestConsole` goldens plus targeted style assertions.

## Done when

Snapshots exist per block shape incl. both match tiers; the summarizer and diff parser have unit tests with adversarial inputs; nothing new escapes unescaped; `scripts/verify.sh` green. No new packages. Wiring into the live area and scrollback stays with T-22.
