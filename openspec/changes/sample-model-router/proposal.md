# Sample: model-router (T-49)

## Why

T-49 (guide row 637): *"Virtual model routes; usage rolls up to the owning run"* — the fitness suite's model-router proof (`docs/spec/extensibility.md` row 119: *"provider + virtual model + nested model call with usage roll-up"*). T-39 built only the **declaration level** and said so in its design: *"Instantiation into `IChatClient` (and routing) lands with the model-router sample card — this change does not build clients."* The durable spec carries the same deferral (*"instantiating clients and routing land with the model-router card"*), and `docs/spec/extensibility.md` promises the full surface: *"`IModelRegistry`: nested model calls by model id with usage charged to the calling run; `RegisterProvider` (IChatClient factory); `RegisterMiddleware` (DelegatingChatClient at defined pipeline slots); virtual models that route per request."* None of that exists in production.

Done-when: the sample works end to end — a virtual model routes per request across two backends, a tool makes a nested model call through the run-bound client, and the usage rolls up to the owning run (events, budget, tree).

## What Changes

- **Core — `IModelRegistry` and the live registry** (`Lunate.Extensibility.Abstractions` contract; implementation in `Lunate.Extensibility`): programmatic provider registration (`IChatClient` factories), middleware at defined pipeline slots (`DelegatingChatClient` wrappers), and **virtual models that route per request** (`Select(request-context) → backend`). Everything registered **at load time** (`IExtensionContext` members, the `RegisterTool`/`RegisterMcpServer` pattern), namespaced `ext/<extension-id>/<id>` so nothing can shadow a catalog model.
- **Core — declared-provider instantiation** (the T-39 deferral): a manifest-declared provider (endpoint + `secretName` + model ids) is instantiated **lazily on first use** into an OpenAI-compatible `IChatClient`; the host reads the secret value from the extension's secrets store (it never crosses to extension code) and the value only lives in the client's headers. Missing secret → teaching error naming provider + `secretName`.
- **Core — run-bound model access with usage roll-up**: `ToolContext.Models` (`IModelAccess.For(modelId) → IChatClient`, stamped per run like `Subagents`/`ToolStubs`); every nested call through it emits `UsageUpdated` **for the owning run**, counts into the run's token budget, and feeds the inherited tree accounting when the run is a child — the same accounting path as the run's own model calls.
- **Resolution order** (documented + tested): virtual model → programmatic provider → declared provider → the session catalog → teaching error listing known ids.
- **Sample `model-router`** (`samples/extensions/model-router`): manifest declares a provider; the extension registers a virtual model `ext/model-router/auto` (backends fast/deep, selector: request size over a setting threshold), a counting middleware at the backend slot, and a tool `router__ask` that makes a nested model call through `ToolContext.Models` and returns the text. Tests prove: routing per request, usage roll-up (events + budget), middleware order, declared-provider instantiation against a local fake endpoint (secret → `Authorization` header, value never visible to the extension), and the teaching errors.
- **Docs/spec**: `extensions` spec — MODIFIED "Model provider registry" (the deferral sentence replaced by the real contract) + ADDED requirements for the live registry, virtual models, and nested-call usage; `docs/spec/extensibility.md` — the core-handles paragraph gains `core/models`, the promise line is aligned with production.

## Not in Scope

- **Out-of-process providers** (T-52's remote host) — untouched.
- **Provider auth flows beyond endpoint + secret** (OAuth device flows etc.) — future.
- **Streaming special-casing** — the registry composes and passes streams through unchanged.
- **Pricing / cost accounting** — token usage only, like the rest of the product.

## Impact

- `Lunate.Extensibility.Abstractions`: `IModelRegistry`-adjacent contract types, registration records, middleware slot enum, `IModelAccess`; `IExtensionContext` gains three registration members — API 1.9 → **1.10** (additive).
- `Lunate.Extensibility`: live `ModelRegistry` (resolution, routing, middleware composition, declared-provider instantiation), wired by `ExtensionSession`.
- `Lunate.Agent`: `ToolContext.Models`; the usage-charging client wrapper; `AgentHarnessOptions.ModelRegistry` handle.
- `Lunate.Ai`: `ChatClientFactory` gains an explicit-key overload (extension secrets are not auth-store entries).
- `Lunate.Coding`: `ExtensionSession`/`HarnessFactory` pass the registry into run options.
- `samples/extensions/model-router/**`; spec + docs updates. No new packages.
