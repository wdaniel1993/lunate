# 0017 (addendum) — Model registry: registration, resolution, secret handling

- Status: accepted — 2026-10-13 (addendum to ADR 0017; T-49 sign-off)
- Date: 2026-10-13
- Relates to: ADR 0017 (extensibility architecture), ADR 0015/0018 (session format — unchanged), ADR 0002 (layering), T-39 (declarations), T-49 (this surface)

## Context

ADR 0017 committed to "a model registry for nested model calls with usage charged to the calling run". T-39 shipped the declaration level only and deferred instantiation and routing. T-49 ships the live surface (providers, middleware, virtual models, declared-provider instantiation, run-bound nested calls). The sign-off fixed several decisions worth recording here: middleware failure policy, the resolution order, and how secrets cross (or rather: do not cross) the extension boundary.

## Decision

1. **Registration surface.** Providers, middleware and virtual models are registered at load time through `IExtensionContext` (`RegisterModelProvider`, `RegisterModelMiddleware`, `RegisterVirtualModel`) — the same load-time-only rule as tools and MCP servers. There is **no `core/models` service handle**: model calls need a run for usage accounting, so access is run-bound through `ToolContext.Models` (`IModelAccess.For(modelId)`), the established per-run seam pattern (`Subagents`, `ToolStubs`).
2. **Layering.** The registry contracts consumed by the harness — `IModelRegistry`, `IModelAccess`, `ModelRouteObserver` — live in `Lunate.Agent` (the harness stamps them into `ToolContext`), because `Lunate.Agent` cannot reference `Lunate.Extensibility.Abstractions`: Abstractions sits *above* Agent and references it. Extensions see these types transitively, exactly like `ToolContext` itself; registration records and the manifest surface stay in Abstractions (extension-facing).
3. **Resolution order and namespacing.** virtual model → programmatic provider → declared provider → session catalog → teaching error listing the known ids. Extension ids are always namespaced `ext/<extension-id>/<id>`: nothing can shadow a catalog model id, and cross-extension collisions are refused by construction.
4. **Middleware.** `DelegatingChatClient` wrappers at two slots (`BeforeRouting`, `AroundBackend`), composed deterministically: extension load order, then priority, then registration order. A middleware exception **fails the request closed** with an error naming the owning extension — never a silent skip. `--no-extensions` bypasses all extension models and middleware.
5. **Secrets.** A declared provider's `secretName` is resolved **host-side** at lazy instantiation; the value only ever reaches the client's request headers. This is **hygiene, not a security boundary**: in-process extensions are fully trusted (ADR 0017, principle 6) — the value simply never *needs* to reach extension code. Missing secret → teaching error naming provider and `secretName`.
6. **Virtual models.** Route per request via their selector; backends may themselves be virtual, checked at first resolve for cycles and a bounded depth (8). Capabilities default to the weakest backend; declarations may only narrow. One client instance may route consecutive calls to different backends.
7. **Usage and attribution.** Nested calls charge the owning run through the **one accounting path** shared with the run's own model calls: `UsageUpdated` carries the resolved model id, counts into the run's token budget, and feeds the inherited tree accounting in child runs.
8. **Session format unchanged.** Assistant entries already carry the model id; routed calls record the **resolved backend**, not the virtual id. ADR 0015/0018 stand — no schema change.

## Consequences

- Frontends can display routing (`ModelRouted` events drive the footer and `/model`).
- Replay stays deterministic: recording happens per backend client, so fixtures live under backend ids and deterministic selectors replay identically.
- A future out-of-process host (T-52) can expose the same registration surface over the wire; the run-bound access remains the only consumption path, which keeps usage accounting sound.
