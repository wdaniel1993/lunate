# add-bash-tool

## Why

Card T-15 (deps T-08): the fourth core tool. The guide fixes the details: shell resolution per platform (table + WSL exclusion + "bash wins whenever it exists"), UTF-8 output, process-tree kill via `Process.Kill(entireProcessTree: true)` on timeout or cancel, truncation, and "the system prompt and the `bash` tool description name the shell". Done-gate: tests pass on all three CI runners.

## What Changes

- **`Lunate.Coding` gains `ShellResolver`** (the guide's resolution table, testable cross-platform via injected probes; settings override seam for T-16) and **`BashTool : ITool`** (`bash`: command required; risk `Execute`; cwd = worktree root; stdin closed; stdout/stderr captured separately with bounded buffers; exit code surfaced; timeout 120 s default with tree kill; cancellation kills the tree; description names the resolved shell).
- Fixture-free by design: tests use temp workspaces and run against the real shells on each CI OS; the resolver's Windows order is unit-tested on every OS via probes.
- Spec delta: `agent-tools` gains the bash-tool requirement + scenarios.

## Impact

- `Lunate.Coding` only (not PublicAPI-tracked — no entries); no new packages; no layering changes; no ADR.
