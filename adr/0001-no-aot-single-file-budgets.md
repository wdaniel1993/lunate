# 0001 — No Native AOT; self-contained single-file releases held to budgets

- Status: accepted
- Date: 2026-10-04

## Context

Sigma ships to developers who should not need to install a .NET runtime. The options were Native AOT versus a self-contained single file with ReadyToRun. AOT would break runtime-loaded extensions, in-process Roslyn and several SDKs — and model and network latency dominate Sigma's runtime anyway.

## Decision

Release builds are self-contained single files (ReadyToRun, compression on) for `osx-arm64`, `win-x64` and `linux-x64` (more targets on demand). Performance is guarded by budgets in CI — startup time and idle memory — not by AOT.

## Consequences

- JIT stays, so runtime extensions (`AssemblyLoadContext`), in-process Roslyn and every SDK remain available.
- Startup and memory budgets become the guardrail; spike S-3 calibrates them.
- Larger binaries and slower cold start than AOT — accepted trade-off.
