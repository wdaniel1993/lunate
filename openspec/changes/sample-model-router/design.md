# Design — sample-model-router

Sign-off items are **Q1–Q7**; the rest are recommendations to accept or amend.

## D1 — The registration surface (`IExtensionContext`, load-time only)

Three new members, the `RegisterTool`/`RegisterMcpServer` pattern (registration only — no processes, sockets or timers at registration time):

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
    Func<IChatClient, IChatClient> Wrap     // DelegatingChatClient wrappers
);

public sealed record VirtualModelRegistration(
    string Id,                              // namespaced
    IReadOnlyList<string> Backends,         // model ids the router may select
    Func<ModelRouteContext, string> Select  // per REQUEST: sees the messages + model id, returns a backend
);
```

`ModelRouteContext`: the requested messages (read-only), the virtual model id, and the request options — enough to route by size, shape or content. Duplicates (provider ids, virtual ids, or any id colliding across extensions) are refused naming both extensions; ≤ 8 providers / 8 virtual models / 16 middlewares per extension (matching the existing registration caps pattern).

## D2 — Middleware slots

- **`BeforeRouting`** wraps the client the *caller* uses: it sees every call, including calls to virtual models (before the route decision).
- **`AroundBackend`** wraps each resolved **backend** client: it sees only the calls that actually reach that backend.

Composition order: same slot = registration order; cross-extension = load order (documented). Wrappers are `DelegatingChatClient`s, so existing MEAI conventions apply. **Q2: accept these two slots, or prefer different/additional ones?**

## D3 — Resolution and the live registry

`ModelRegistry` (in `Lunate.Extensibility`) owns the live surface:

```csharp
public interface IModelRegistry
{
    IChatClient Resolve(string modelId);    // composed: middleware + routing + backends
}
```

Resolution order, fixed and tested: **virtual model → programmatic provider → declared provider → session catalog → teaching error** (the error lists the known ids, bounded). Extension-registered ids are always namespaced `ext/<extension-id>/<id>`, so **nothing can shadow a catalog model id** and cross-extension collisions are impossible by construction. A virtual model's `Backends` are validated **at first resolve**, not at registration (load order makes registration-time validation impossible): an unknown backend → teaching error naming the virtual model and the backend. The client returned for a virtual model is a **router client**: every `GetResponseAsync` call evaluates `Select` independently — routing is per request by construction, and the same client instance may route consecutive calls to different backends.

**Q3: resolution order + namespacing + first-resolve validation — accept?**

## D4 — Declared-provider instantiation (the T-39 deferral)

For a manifest-declared provider (`endpoint`, `secretName`, `modelIds`):

- Instantiation is **lazy on first use**; the result is cached per model id.
- The **host** reads the secret via `IExtensionSecrets.TryGet(secretName)` and builds an OpenAI-compatible client through `ChatClientFactory` (new overload taking the key explicitly — extension secrets are not auth-store entries, so `AuthRef` is not used). The value lives only in the client's `Authorization` header; extension code never receives it.
- Defaults for declared models (the manifest carries no metadata): `ContextWindow` 128 000, `SupportsTools` true. **Q5: accept or adjust?**
- A missing secret or an unusable endpoint is a **teaching error naming the provider id and `secretName`** (never a value), delivered when the model is first used.

## D5 — Run-bound access and usage roll-up (`ToolContext.Models`)

```csharp
public interface IModelAccess
{
    IChatClient For(string modelId);        // resolved + bound to the owning run
}
```

Stamped by the harness per run (`ToolContext.Models`, null outside a run or when no registry is configured) — the established per-run seam pattern. Every nested call through the returned client:

- emits `UsageUpdated(owningRunId, details)` when the provider reports usage,
- counts into the run's token budget (`_tokenBudgetUsed`),
- feeds the inherited tree accounting when the run is a child run,

i.e. the **same accounting path as the run's own model calls** (shared helper). Unlike compaction summarization (deliberately not emitted), nested-call usage is fully charged — that is the point of the card. **Q4: accept?** **Q6: the `ToolContext.Models` seam (recommended) vs an ambient context?**

## D6 — The sample

`samples/extensions/model-router/`: `ModelRouterExtension` + tests.

- Manifest declares provider `router-local` (endpoint `http://127.0.0.1:11434/v1`, `secretName` `router-key`, model ids `fast`, `deep`).
- The extension registers virtual model `ext/model-router/auto` (backends `ext/model-router/router-local/fast` + `.../deep`), selector: request size over a settings threshold (`deepThresholdChars`, default 4 000) → `deep`, else `fast`.
- A counting middleware at `AroundBackend` records calls per backend (observable proof of slot semantics).
- Tool `router__ask` (prompt, required): calls `ctx.Models.For("ext/model-router/auto")`, returns the response text; annotations read-only (it only asks a model), settings file pattern of the other samples.
- Tests: routing per request (short → fast, long → deep, same client), usage roll-up (the run's `UsageUpdated` carries the nested tokens; the token budget counts them), middleware sees backend calls only, unknown model / unknown backend / missing secret teaching errors, and **declared-provider instantiation against a local fake OpenAI endpoint** (`HttpListener` in the test assembly): the request carries the secret as `Authorization: Bearer …`, and the extension-visible surface never contains the value.

## D7 — Where the code lives

- **Abstractions**: `IModelRegistry`, `IModelAccess`, the registration records, the slot enum; `IExtensionContext` members (API 1.9 → 1.10, additive).
- **Extensibility**: `ModelRegistry` (live), `ExtensionLoader` records registrations, declared providers instantiate here.
- **Agent**: `ToolContext.Models`, the usage-charging wrapper (a `DelegatingChatClient`), `AgentHarnessOptions.ModelRegistry`.
- **Ai**: `ChatClientFactory` explicit-key overload.
- **Coding**: `ExtensionSession` hands the registry to run composition.

## D8 — Errors

Every failure is a teaching error, never a crash: unknown model (lists known ids), unknown virtual backend, missing secret (names provider + secretName), provider factory throwing (message + provider id; logged once). Provider exceptions during a call surface as the provider client's own errors (unchanged).

## D9 — Tests (beyond the sample)

Core units: resolution order incl. the catalog fallback; namespacing and duplicate refusals; registration caps; middleware composition order; per-request routing with one client instance; usage charging path (event + budget + tree) against a scripted harness; `For` outside a run → null seam.

## D10 — Non-goals

T-52 remote host; provider auth flows; streaming special-casing; pricing/costs; per-model concurrency limits beyond what provider clients already do.

## Open sign-off questions

- **Q1** — Registration members on `IExtensionContext` (recommended — load-time-only rule) vs registering through a core service handle?
- **Q2** — Middleware slots `{BeforeRouting, AroundBackend}` (D2) — accept or adjust?
- **Q3** — Resolution order + `ext/<extension-id>/<id>` namespacing + first-resolve backend validation (D3) — accept?
- **Q4** — Usage semantics: nested calls emit `UsageUpdated` on the owning run, count into its budget and tree (D5) — accept?
- **Q5** — Declared-model defaults: context window 128 000, tools supported, lazy instantiation, missing secret = teaching error (D4) — accept or adjust?
- **Q6** — `ToolContext.Models` seam (recommended) vs ambient run context?
- **Q7** — No new ADR (extends ADR-0017's services model); flag if you want one.
