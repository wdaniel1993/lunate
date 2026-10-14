# Design — sample-model-router

Sign-off: 2026-10-13, with adjustments (Q3, Q5, Q7 amended; Q1/Q2/Q4/Q6 confirmed with additions). **SIGN-OFF:** notes record Daniel's decisions.

## D1 — The registration surface (`IExtensionContext`, load-time only)

**SIGN-OFF Q1: yes.** Three new members, the `RegisterTool`/`RegisterMcpServer` pattern (registration only — no processes, sockets or timers at registration time):

```csharp
void RegisterModelProvider(ExtensionModelProvider provider);
void RegisterModelMiddleware(ExtensionModelMiddleware middleware);
void RegisterVirtualModel(VirtualModelRegistration model);
```

```csharp
public sealed record ExtensionModelProvider(
    string Id,                              // [a-z0-9-]+, namespaced ext/<extension-id>/<id>
    IReadOnlyList<string> ModelIds,         // the ids this provider serves
    Func<IChatClient> Create                // called once per model id, lazily, at first use
);

public enum ModelMiddlewareSlot { BeforeRouting, AroundBackend }

public sealed record ExtensionModelMiddleware(
    ModelMiddlewareSlot Slot,
    int Priority,                           // SIGN-OFF Q2: deterministic order — load order, then priority
    Func<IChatClient, IChatClient> Wrap     // DelegatingChatClient wrappers
);

public sealed record VirtualModelRegistration(
    string Id,                              // namespaced
    IReadOnlyList<string> Backends,         // model ids (may themselves be virtual) the router may select
    Func<ModelRouteContext, string> Select, // per REQUEST: messages + model id → backend id
    int? ContextWindow = null,              // SIGN-OFF Q3: capabilities default to the weakest backend;
    bool? SupportsTools = null              // declarations are validated against the backends
);
```

`ModelRouteContext`: the requested messages (read-only), the virtual model id, and request options. Duplicates (any id colliding across extensions) are refused naming both; ≤ 8 providers / 8 virtual models / 16 middlewares per extension (existing registration-cap pattern).

## D2 — Middleware: slots, order, failure policy

**SIGN-OFF Q2: yes, plus:**

- **`BeforeRouting`** wraps the client the caller uses (sees every call, including virtual-model calls); **`AroundBackend`** wraps each resolved backend client (only calls that reach a backend).
- **Deterministic composition order: extension load order, then middleware `Priority` (ascending), then registration order.** First in the chain = outermost wrapper. Tested.
- **Middleware exceptions fail the request closed** with an error naming the owning extension — never a silent skip. Tested (a throwing middleware produces an error naming the extension, and no request reaches the provider).
- **`--no-extensions` escapes everything**: with extensions suppressed (the existing CLI flag), the registry is empty and requests go straight to the catalog. Tested.

## D3 — Resolution and the live registry

`ModelRegistry` (in `Lunate.Extensibility`) owns the live surface: `IChatClient Resolve(string modelId)`. Resolution order, fixed and tested: **virtual model → programmatic provider → declared provider → session catalog → teaching error** (lists known ids, bounded). Extension ids are always namespaced `ext/<extension-id>/<id>` — nothing can shadow a catalog model id.

**SIGN-OFF Q3: yes, plus:**

- **Virtual → virtual routing is checked at first resolve**: the backend chain is walked; a **cycle** or a chain deeper than **8** is a teaching error naming the chain.
- **Virtual-model capabilities** default to the **weakest backend** (minimum context window; tools only when every backend supports them); a declaration may only **narrow** — a declared window above the weakest backend's, or `SupportsTools: true` without every backend supporting tools, is refused at first resolve with a teaching error.
- **Sessions record the resolved backend per response, not the virtual id.** The session format already carries the model id on assistant entries (agent-sessions spec) — routing populates it with the **resolved backend**; no format change (so no full ADR, per Q7). Recording/replay runs per backend client, so fixtures land under backend ids and deterministic selectors replay identically.
- **The footer and `/model` show routing**: a routed call emits `ModelRouted` (D5); the footer shows `auto → deep` (last resolution) when the session's model is a virtual/extension model, and `/model` shows the same line for the current model.
- A virtual model's `Backends` validate at first resolve (load order makes registration-time validation impossible): unknown backend → teaching error naming the virtual model and the backend.

The client returned for a virtual model is a **router client**: every `GetResponseAsync` evaluates `Select` independently — routing is per request by construction, and one client instance may route consecutive calls to different backends.

## D4 — Declared-provider instantiation (the T-39 deferral)

**SIGN-OFF Q5, amended:**

- **Manifest declarations gain `contextWindow` (required, positive integer) and `supportsTools` (optional, default true).** Rationale from sign-off: local models are often 8–32k, and a wrong (too large) window makes compaction trigger too late. A missing `contextWindow` is a validation error naming the field.
- Instantiation is **lazy on first use**, cached per model id; the **host** reads the secret via `IExtensionSecrets.TryGet(secretName)` and builds an OpenAI-compatible client through `ChatClientFactory` (explicit-key overload). The value lives only in the client's request headers.
- **Missing secret → teaching error naming the provider id and `secretName`** (never a value), on first use.
- A provider error reporting that **tools are not supported** surfaces as a teaching error naming the provider and the `supportsTools` declaration (so the fix is findable).
- **Docs wording (sign-off):** the host-side secret read is **hygiene, not a security boundary** — in-process extensions are fully trusted (ADR-0017); the value simply never needs to reach extension code.

## D5 — Run-bound access, usage roll-up, and the routing event

**SIGN-OFF Q4 + Q6: yes, plus.** `ToolContext.Models` (`IModelAccess.For(modelId) → IChatClient`), stamped per run (null outside a run / without a registry). Every nested call through it:

- emits `UsageUpdated` for the owning run, **and `UsageUpdated` gains `ModelId`** (additive) — the **resolved** model (the backend for routed calls; the requested id otherwise) for per-model attribution;
- counts into the run's token budget and feeds the inherited tree accounting when the run is a child — **inside subagents, a tool's nested call charges the child run and rolls up** (explicit scenario);
- **routed calls emit `ModelRouted(runId, requestedModel, resolvedModel)`** (a new run-scoped extension event) when the resolution happens, so frontends can display routing. Mechanism: the run-bound wrapper scopes a route observer around each call (an ambient defined in the abstractions); the router reports each resolution to it.

## D6 — The sample

`samples/extensions/model-router/`: `ModelRouterExtension` + tests.

- Manifest declares provider `router-local` (endpoint `http://127.0.0.1:11434/v1`, `secretName` `router-key`, model ids `fast`/`deep`, `contextWindow` 32 768) — the declared instantiation is proven against a **local fake OpenAI endpoint** (`HttpListener`): the request carries the secret as its authorization header; no extension-visible surface carries the value.
- Virtual model `ext/model-router/auto` (backends fast/deep; selector: request size over the `deepThresholdChars` setting, default 4 000); capabilities derived (weakest backend).
- A counting middleware at `AroundBackend` records calls per backend (observable slot proof); a throwing-middleware test proves fail-closed.
- Tool `router__ask` (prompt, required; read-only): calls `ctx.Models.For("ext/model-router/auto")`, returns the response text.
- Tests: routing per request (short → fast, long → deep, one client instance); usage roll-up (run `UsageUpdated` carries nested tokens **and the resolved `ModelId`**; budget counts them; child-run variant rolls into the tree); middleware order (priority) and fail-closed; `ModelRouted` events; footer/`/model` display (TUI test seam); teaching errors (unknown model/backend, cycle, depth, missing secret, tools-unsupported guidance); declared instantiation (auth header, value never visible).

## D7 — Where the code lives

- **Abstractions**: contract types per D1, `IModelRegistry`, `IModelAccess`, the route observer ambient; `IExtensionContext` members (API 1.9 → 1.10, additive).
- **Extensibility**: `ModelRegistry` (resolution, routing incl. cycle/depth + capabilities, middleware composition, declared instantiation), manifest validation (`contextWindow`/`supportsTools`), `ModelProviderDescriptor` fields.
- **Agent**: `ToolContext.Models`; the usage-charging + route-observing wrapper; `AgentHarnessOptions.ModelRegistry`; `UsageUpdated.ModelId`; `ModelRouted`; assistant-entry model = resolved backend.
- **Ai**: `ChatClientFactory` explicit-key overload.
- **Coding/TUI**: registry → run options; footer `auto → deep`; `/model` routing line.
- **Docs**: `adr/0017-addendum-model-registry.md` (**SIGN-OFF Q7**: addendum covering middleware failure policy, resolution order, secret handling; no full ADR — the session format does not change).

## D8 — Errors

Every failure is a teaching error, never a crash: unknown model (known ids listed), unknown backend, cycle/depth, invalid capability declaration, missing secret, tools-unsupported guidance, middleware exception (names the extension, fails closed). Provider exceptions during a call surface unchanged.

## D9 — Tests (beyond the sample)

Core units: resolution order incl. catalog fallback; namespacing/duplicates/caps; middleware order + fail-closed; per-request routing with one client; cycle/depth; capability derivation/validation; usage path (event + `ModelId` + budget + tree) against a scripted harness; `ModelRouted` emission; `For` outside a run → null.

## D10 — Non-goals

T-52 remote host; provider auth flows; streaming special-casing; pricing/costs; per-model concurrency beyond provider clients.

## Deviations

1. **Registry contracts live in `Lunate.Agent`, not Abstractions** (`src/Lunate.Agent/ModelRouting.cs`: `IModelRegistry`, `IModelAccess`, `ModelRouteObserver`). D7's placement was wrong: the harness stamps these into `ToolContext`, and `Lunate.Agent` cannot reference `Lunate.Extensibility.Abstractions` — Abstractions sits *above* Agent and references it. Extensions see them transitively (exactly like `ToolContext` itself); registration records and the manifest surface stay in Abstractions.
2. **The live `ModelRegistry` absorbed the declaration-level `ModelProviderRegistry`** — its listing role is now `ModelRegistry.List()` (declared providers, registration order); tests renamed accordingly.
3. **Fake endpoint is a raw `TcpListener`** with an ephemeral `127.0.0.1` port instead of the design's `HttpListener` (http.sys URL ACLs make `HttpListener` unreliable on Windows CI).
4. **The sample test rewrites the manifest endpoint** to the ephemeral port before loading (the fake endpoint picks the port at runtime).
5. **Programmatic providers have no declared capabilities**: `FindCapabilities` reports window 0 (unknown — percent omitted) and tools true; declared providers use the manifest's `contextWindow`/`supportsTools`; virtual models derive from backends.
6. **`IChatClientFactory` gained an explicit-key overload** (Ai-side, additive) so declared providers can use extension secrets without an auth-store entry.
7. **Tools-unsupported is a pre-flight refusal** for `supportsTools: false` (teaching error naming the declaration) rather than translating a provider-side error.
