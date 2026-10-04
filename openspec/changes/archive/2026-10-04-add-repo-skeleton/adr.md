# ADR Review Manifest

- Status: completed
- Review date: 2026-10-04

## Review Summary

ADR review completed for this change. `adr/` had no in-force ADRs before this change; the skeleton makes two guide-level decisions concrete and durable, so they are recorded as the repository's first ADRs.

## In-Force ADRs Reviewed

- None — `<repo>/adr/` had no in-force ADRs before this change.

## New Durable ADRs Created

- `adr/0001-no-aot-single-file-budgets.md` — release shape: no Native AOT; self-contained single-file (ReadyToRun), held to startup/memory budgets.
- `adr/0002-layering-and-meai-model-types.md` — downward-only project graph; Microsoft.Extensions.AI model types everywhere; own loop without `FunctionInvokingChatClient` / `AIFunctionFactory`.
