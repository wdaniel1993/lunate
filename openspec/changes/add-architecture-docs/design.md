## Context

The guide's architecture section describes the layers in prose and references an embedded diagram that has no maintained artifact in the repo. The four core pieces (pipeline, events, tools, loop) have landed, so the picture is stable enough to draw. The repo-quality change established the documentation map; this change adds the visual overview the map points to.

## Goals / Non-Goals

**Goals:** one page that shows components, flows and boundaries; diagrams that are text (diffable, reviewable, AI-maintainable) and render natively on GitHub; every diagram small and linked to its spec/ADR home; a spec rule that keeps the page current.

**Non-Goals:** diagrams for unbuilt cards; a rendered-docs site; diagram validation tooling (no new dependencies — Mermaid fences are plain text); any code change.

## Decisions

- **Mermaid in Markdown**: GitHub renders Mermaid natively, the diagrams are plain text (reviewable, diffable, no binary rot), and no toolchain is needed in the gate. SVG exports were rejected for exactly that rot/review cost.
- **Four diagrams, each small**: system map; model pipeline order; one run (sequence, with spans noted); event path. Each is capped to roughly a screen and links to the spec or ADR that owns its detail — the diagram is the overview, specs stay the source of truth.
- **Accuracy is pinned to code, not prose**: the system map's edges are taken from the actual `ProjectReference` graph; the pipeline order from `ChatClientFactory`; the run sequence from `AgentHarness` and the `agent-loop` spec; the event path from `AgentEventChannel` and `RunAsync`. The review verifies each edge against the sources.
- **Keep-current rule** (the spec requirement): a change that alters the pictured structure updates `docs/architecture.md` in the same change. Reviewers can check it cheaply because the diagrams are text.
- **No ADR**: documentation layout is not an architecture decision; the durable rule lives in the `repo-quality` spec, and ADR-0011 already records the documentation tooling.

## Risks / Trade-offs

- [Diagrams drift from code] → the keep-current rule plus text-diffable diagrams; the reviewer checks edges against sources in this change.
- [Mermaid syntax mistakes render as broken blocks] → lint cannot catch them, but the review renders/parses the blocks; keep the syntax simple (flowchart and sequenceDiagram only).
- [Page duplicates the guide] → one home per fact: the page shows structure, the guide keeps the plan and rationale, specs keep behavior; the page links both.

## Migration Plan

Not applicable — additive.

## Open Questions

- None blocking.
