# 0016 — Workspace boundary for file tools

- Status: proposed — 2026-10-07
- Date: 2026-10-07
- Relates to: ADR 0012 (tool contract); guide "Security and safety"

## Context

The file tools (`read`, `write`, `edit`) run with the user's rights on model-chosen paths. The guide fixes the policy — resolve real paths including symlinks, refuse anything outside the working directory unless `--allow-path` is given, compare case-insensitively on Windows and default macOS file systems — but not the mechanism. A shared, testable resolution is needed before the first tool ships, because the boundary is the security surface.

## Decision

- **Canonical resolution.** Every path is lexically normalized, then canonicalized: each existing component that is a link is replaced by its final target; a non-existing tail (write targets) is appended after the deepest existing ancestor is canonicalized. The roots are canonicalized the same way.
- **Final-target rule.** A path is inside when its canonical target equals an allowed root or sits under it. A symlink inside the workspace that points outside is refused; one that points inside is allowed.
- **Allowed roots.** The working directory plus explicitly configured extra roots (the CLI's `--allow-path` maps to these).
- **Case comparison.** `OrdinalIgnoreCase` on Windows and macOS, `Ordinal` on Linux — following the file system defaults the guide names.
- **One implementation.** All file tools share the same `Workspace`; position or path logic never re-implements the check.
- **Check at call time, not a sandbox.** This is a guard rail for a single-user CLI tool against model mistakes, not an isolation boundary: TOCTOU races and paths reached through other means are accepted. `bash` is deliberately not boundary-checked; the approval policy is its guard (T-16). Extensions run in-process and are trusted like Pi's.

## Consequences

- Refusals are predictable and instructive: they name the path, the resolved target and the allowed roots, so the model can correct course.
- Over-refusal in the safe direction (lexical `..` collapse before symlink resolution) is accepted; the error still tells the model what happened.
- The boundary behavior is pinned by tests including symlinks and case-insensitive file systems (T-12's done-when).
- If a future feature needs path resolution outside these roots (for example editor-driven reads in ACP mode), it extends the allowed roots explicitly rather than bypassing the check.
