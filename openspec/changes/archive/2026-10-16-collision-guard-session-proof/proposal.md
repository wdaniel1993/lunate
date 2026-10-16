# Collision guard: session-level proof + dedupe scope

## Why

The `add-extension-tools` re-review left two non-blocking follow-ups on the tool-name collision
guard (`ToolComposition.AddSkippingTaken`):

1. **No session-level proof.** The unit tests cover both overloads in isolation; nothing proves
   that, through *real session composition*, an extension tool actually beats a colliding MCP
   tool — the ordering claim the guard's doc comment makes ("extension tools compose first, so an
   extension tool wins over a later MCP tool with the same name").
2. **The guard's dedupe scope is undecided.** The skip is source-agnostic: it also skips
   non-extension-vs-MCP duplicates. That is either fine-by-design or should be narrowed — nobody
   decided.

## What changes

- **A session-level end-to-end test**: a direct extension tool named `test__echo` colliding with a
  configured MCP server `test` exposing `echo` (also `test__echo`) — through the real session
  pipeline: one declaration wins (the extension's), the skip is warned once naming both sources,
  and executing `test__echo` runs the extension handler, not the MCP server.
- **A documented scope decision** on `ToolComposition` (and here): the source-agnostic skip is
  fine-by-design — rationale in `design.md`.

No product behaviour changes; tests + words only (`skip_specs`).
