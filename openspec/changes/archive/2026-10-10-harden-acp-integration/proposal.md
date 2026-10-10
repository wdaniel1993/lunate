# Proposal: Harden the ACP integration (T-28 follow-ups)

## Why

The T-28 review approved the merge with three non-blocking robustness items and one stale sketch: the read tool now loads entire binaries into memory (the old local probe is gone), client round trips have no timeout (a stalled editor hangs the connection), and the `Exists` read-probe masks every error as "not found". While preparing the fix, a spec divergence surfaced: `agent-files` pins binary detection to the first 8,192 bytes, while the current implementation scans the whole decoded text.

## What changes

- **Binary probe restored (local)**: `ITextFileAccess` gains a bounded prefix probe; `ReadTool` probes before loading (fast rejection with the exact size) and falls back to the full-text check where the provider cannot probe (client-backed reads). The `agent-files` read requirement is updated to state both provider behaviours — resolving the divergence.
- **Bounded round trips**: file requests through the client time out (default 30 s) and fail as I/O errors; permission requests time out (default 10 min) and decline — a stalled editor degrades, it does not hang.
- **Error taxonomy**: the client's missing-file error maps to not-found (never masked by `Exists`, which rethrows everything else); other client failures surface as I/O errors.
- **Guide refresh**: the `IAcpServer` sketch in `docs/guide.md` catches up with the shipped `AcpSessionContext` factory shape.

## Done when

The probe, timeout and taxonomy behaviours are covered by tests (local + client-backed paths), the spec divergence is resolved in `agent-files`, and `scripts/verify.sh` is green.
