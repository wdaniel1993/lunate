## Why

Pi's extension API is the bar: transforming and blocking hooks, tool exposure modes, nested tool calls with parent ids, MCP servers and providers registered by extensions, compaction hooks, continuation at turn boundaries, session entries, renderers, mode-aware UI. Lunate must be able to host extensions of that complexity — subagents, memory providers, LSP integrations, code mode, permission gates, model routers. T-07 to T-11 have landed, so changes to events, tools and sessions are still cheap, versioned migrations; the formats must carry the extension metadata now, before frontends and stored sessions multiply.

## What Changes

- **ADR-0017** (proposed): the extensibility architecture — principles, the semver'd contract assembly, ALC sharing, the trust model (in-process trusted; ALC isolates dependencies, not permissions), the JSON-serializable hook boundary, and the out-of-process constraint.
- **`docs/spec/extensibility.md`**: the working spec — contract and manifest, lifecycle, the hook catalogue with semantics and failure policy, the extended tool model, long-lived services, UI by mode, code mode and hosted tools, out-of-process constraints, trust/performance/testing rules, and the fitness suite of eight reference extensions.
- **Guide**: the Extensions section reframed onto the contract assembly; the T-24/T-34 cards replaced by the extensibility series T-36…T-51; subagents stay a non-goal for the core ("subagent-ready core" instead); the fitness suite named as the proving ground.
- **Docs map**: `docs/README.md` gains the new spec.

Part A (the format migrations: events, tools, session schema v2, goldens) is the next change — `add-extension-formats` — and references this ADR and spec.

## Capabilities

### New Capabilities
- None (documentation change).

### Modified Capabilities
- None (behavioural specs land with `add-extension-formats`).

## Impact

- New: `adr/0017-extensibility-architecture.md`, `docs/spec/extensibility.md`. Edited: `docs/guide.md` (extensions section, non-goals, cards, tech stack row), `docs/README.md`. No `src/` changes.
