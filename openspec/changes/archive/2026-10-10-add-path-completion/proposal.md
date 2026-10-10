# Proposal: path completion and file index (T-53)

## Why

The input line completes `/commands` but not `@paths` — the guide's InputLine pins both, and the T-22 completion work explicitly deferred `@path` completion to T-53 (the tui spec says so). Users referencing files in prompts currently type paths blind.

## What changes

- **File index** (`Lunate.Coding`): a workspace file index built **lazily** (never at startup), asynchronously, honouring `.gitignore` files (root and nested; negation, directory-only, anchoring) while always skipping `.git`; bounded to 200,000 entries with a truncation flag; directory symlinks not followed. The filesystem is behind a small seam so ignore semantics and bounds are tested deterministically.
- **Tab completion for `@paths`**: when the cursor sits at the end of an `@token`, Tab completes it against the index (single match → replace, several → longest common prefix, no progress → dim candidates notice capped at 20); while the index builds, an indexing notice appears and the build starts. Non-`@` text keeps today's slash-command behaviour.
- **Budget check**: a dedicated perf test (trait-excluded from the main passes) on a generated 20,000-file tree asserting build ≤ 5 s and query ≤ 50 ms, wired as its own step in `verify.sh`/`verify.ps1` so the done-when ("stays within the startup and latency budgets") is mechanically proven; the startup budget is unaffected because the index never builds at startup.

## Done when

Ignore semantics, laziness, bounds and the completion flow are covered by tests; the 20k-file budget step runs green in the gate on all three OSes; `scripts/verify.sh` green.
