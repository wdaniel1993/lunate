## Why

The guide references an architecture diagram ("embedded content") that does not exist as a maintained artifact in the repository, and nothing visual shows how the pieces that now exist fit together. With the model pipeline (T-04 to T-06), the events (T-07), the tools (T-08) and the loop (T-09) landed, the system is finally diagrammable as a whole — and every future session (human or AI) benefits from one page that shows the components, the flows and where each detail lives.

## What Changes

- **`docs/architecture.md`** — the visual overview, four small Mermaid diagrams, each linking to the spec or ADR that owns its detail:
  1. the system map (projects, dependency directions, external boundaries — providers, MCP, ACP);
  2. the model pipeline order (OpenTelemetry, logging, accumulator, recorder, provider — and where replay replaces);
  3. one run as a sequence (harness, model calls, tool execution, events, spans);
  4. the event path from emission to consumers.
- **Links**: `docs/README.md` gains the entry; the guide's architecture placeholder becomes a pointer to the page; the README's documentation list gains it.
- **`repo-quality` spec** gains an "Architecture documentation" requirement: the page exists, stays small per diagram, links its homes, and is updated in the same change that alters the pictured structure.
- Out of scope: sequence diagrams for cards that are not built yet (sessions, compaction, MCP handshakes); no code changes; no new tooling (Mermaid renders natively on GitHub).

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `repo-quality`: adds the architecture documentation requirement.

## Impact

- New: `docs/architecture.md`. Modified: `docs/README.md`, `docs/guide.md` (placeholder → link), `README.md`. No code, no packages. No ADR: this is a documentation layout convention, and its durable home is the spec requirement (one home per fact — ADR-0011 already owns the docs tooling).
