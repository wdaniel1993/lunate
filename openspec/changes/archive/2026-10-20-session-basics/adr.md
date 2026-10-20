# ADR Review Manifest

- Status: completed
- Review date: 2026-10-20

## Review Summary

ADR review completed for change `session-basics`. The change reuses the append-only session store and its existing entry kinds (header `cwd`, `promptSection` for directives) and adds no new architectural boundary: the CLI parser, store queries, title sidecar and two slash commands sit inside the current layering. No in-force ADR is superseded or contradicted.

## In-Force ADRs Reviewed

- ADR-0002 — Three-layer project graph and Microsoft.Extensions.AI model types.
- ADR-0003 — Loop: own loop on `IChatClient`.
- ADR-0013 — The agent loop and harness surface (session hooks, prompt sections).
- ADR-0015 — Session format pinned by golden files (unchanged; no golden edit).
- ADR-0016 — Workspace boundary for file tools (selection compares canonical paths only).
- ADR-0018 — Session schema 2 (no bump; title lives in a sidecar).
- ADR-0021 — Per-run workspaces and per-workspace services (unchanged).

## New Durable ADRs Created

- None — no major durable architectural decisions were introduced. The sidecar title and directive recording are tactical choices recorded in `design.md` (D4, D5) and can be revisited without a new architecture; a future pricing field in `ModelInfo` would be a separate, ADR-worthy decision.
