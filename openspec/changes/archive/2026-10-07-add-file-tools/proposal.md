## Why

Card T-12: the agent's first real tools. File access is the most-used and most dangerous surface, so the workspace boundary ships with it, not after. The guide fixes the policy (Security: file tools resolve real paths including symlinks and refuse anything outside the working directory unless `--allow-path` is given; boundary checks are case-insensitive on Windows and on default macOS file systems) and the shapes (the four-tools table: `read` numbering/footer/errors, `write` result line and diff in `Details`). Nothing exists yet: `Lunate.Coding` is the CLI skeleton and there is no path resolution anywhere.

## What Changes

- **Workspace** (`Lunate.Coding`): canonical path resolution (symlinks resolved for every existing component; the final target must be inside), boundary check against the working directory plus optional extra roots, case rules per platform, and instructing errors naming the resolved path and the roots.
- **`read` tool**: `path`, `offset` (1-based, default 1), `limit` (default and max 2,000); numbered lines with a continuation footer; errors for not-found, binary (size given), directory (use `bash ls`) and outside-workspace.
- **`write` tool**: `path`, `content`; creates parent directories; result `wrote N lines to <path> (created|replaced)`; the content is written exactly as given (UTF-8, no BOM, no newline munging); a unified diff in `Details` (UI-only).
- **ADR-0016**: the workspace boundary policy — canonical resolution, final-target rule, per-platform case comparison, extra roots, check-at-call-time (not a sandbox).
- Out of scope: `edit` (T-13/T-14), `bash` (T-15), approval-policy levels (T-16), CLI flags incl. `--allow-path` wiring (T-17/T-32).

## Capabilities

### New Capabilities
- `agent-files`: the workspace boundary for file tools, and the `read` and `write` tools (parameters, output shapes, errors).

### Modified Capabilities
- None.

## Impact

- New in `src/Lunate.Coding/`: `Workspace.cs`, `ReadTool.cs`, `WriteTool.cs`, `WriteDetails.cs`, internal `TextFile.cs` and `LineDiff.cs`. New coverage in `tests/Lunate.Coding.Tests/` (workspace boundary incl. symlinks and case-insensitive file systems; read and write formats). No new packages. `Lunate.Coding` stays untracked by PublicAPI (app assembly, consistent with the tracked libraries).
