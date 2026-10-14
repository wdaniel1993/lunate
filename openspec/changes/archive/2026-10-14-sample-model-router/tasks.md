# Tasks — sample-model-router

## 1. Contract (`Lunate.Extensibility.Abstractions`)

- [x] 1.1 `IModelRegistry`, `IModelAccess`, `ExtensionModelProvider`, `ExtensionModelMiddleware` (with `Priority`), `ModelMiddlewareSlot`, `VirtualModelRegistration` (with optional capability declarations), `ModelRouteContext`, the route-observer ambient per D1/D5 (XML docs; namespacing rules stated); `IExtensionContext` gains `RegisterModelProvider`/`RegisterModelMiddleware`/`RegisterVirtualModel`; API 1.9 → 1.10; PublicAPI.Unshipped; ExtensionApiTests updated.
- [x] 1.2 Registration validation: id shape + namespacing, duplicates refused naming both extensions, caps (8/8/16); units.
- [x] 1.3 Manifest format: `modelProviders[].contextWindow` required (positive int) + optional `supportsTools` (default true); validation errors name the field; `ModelProviderDescriptor` fields; units.

## 2. Live registry (`Lunate.Extensibility`)

- [x] 2.1 `ModelRegistry`: resolution order (virtual → programmatic provider → declared provider → catalog → teaching error), namespaced ids, `Resolve` composition (middleware chain per D2 + router clients), first-resolve validation (unknown backend, virtual→virtual cycle, depth > 8, capability narrowing), factory-exception teaching errors.
- [x] 2.2 Middleware composition per D2: load order → priority → registration; fail-closed on middleware exceptions (error names the extension; nothing reaches the provider); `--no-extensions` bypass verified.
- [x] 2.3 Declared-provider instantiation (D4): lazy per model id, host-side secret read, `ChatClientFactory` explicit-key overload (`Lunate.Ai`), tools-unsupported teaching error naming provider + `supportsTools`, missing-secret teaching error.
- [x] 2.4 Units: resolution order incl. catalog fallback; middleware order + fail-closed; per-request routing with one client; cycle/depth; capability derivation/validation; unknown backend; missing secret; factory throws.

## 3. Run-bound access + events (`Lunate.Agent`)

- [x] 3.1 `ToolContext.Models` stamped by the harness (`AgentHarnessOptions.ModelRegistry`); null outside a run / without a registry.
- [x] 3.2 Usage-charging + route-observing wrapper: nested calls emit `UsageUpdated(owningRunId)` **with `ModelId`** (resolved backend for routed calls) + count into `_tokenBudgetUsed` + inherited tree accounting — shared with the run's own accounting path (D5); routed calls emit `ModelRouted(requested, resolved)`.
- [x] 3.3 `UsageUpdated.ModelId` (additive) + `ModelRouted` event; assistant-entry model = resolved backend (agent-sessions delta); PublicAPI.
- [x] 3.4 Units: `For` outside a run → null; usage charged (event + ModelId + budget + tree) with a scripted registry/harness; `ModelRouted` emission; resolution failure teaching error through the seam.

## 4. Wiring + frontends (`Lunate.Coding`, TUI)

- [x] 4.1 `ExtensionSession`/`HarnessFactory`: registry → run options; integration test (an extension-registered provider resolvable from a real composed session).
- [x] 4.2 TUI: footer model segment shows the last route (`auto → deep`) for virtual/extension models (updates on `ModelRouted`); `/model` picker current-model line shows the routing line; test seams per the tui delta.

## 5. Sample (`samples/extensions/model-router`)

- [x] 5.1 Extension + manifest (declared provider `router-local`, endpoint + `router-key` + ids fast/deep + `contextWindow` 32768), virtual model `auto` (selector + `deepThresholdChars` setting, capabilities derived), counting middleware at `AroundBackend`, tool `router__ask` (read-only), README (what it proves, honest limits incl. the hygiene note).
- [x] 5.2 Tests: routing per request (short → fast, long → deep, same client); usage roll-up (events + resolved `ModelId` + budget; child-run variant via subagent); middleware order (priority) + fail-closed; `ModelRouted` + footer display; teaching errors (unknown model/backend, cycle, depth, missing secret, tools-unsupported guidance); declared-provider instantiation against a local fake OpenAI endpoint (HttpListener): authorization header carries the secret, the extension surface never shows the value.
- [x] 5.3 Solution entries + AssemblyInfo parallelization pattern.

## 6. Spec + docs + ADR addendum

- [x] 6.1 Deltas: `extensions` (MODIFIED provider registry + ADDED middleware/virtual-models + ADDED nested-call charging), `agent-events` (MODIFIED closed set: `ModelRouted`, `UsageUpdated.ModelId`), `agent-sessions` (MODIFIED resolved model on assistant entries), `tui` (MODIFIED footer + slash commands) — already written; keep implementation consistent.
- [x] 6.2 `docs/spec/extensibility.md`: the model-registry bullet aligned with production (registration via `IExtensionContext`, run-bound `ToolContext.Models`, resolution order). Resolution: **no `core/models` handle** — model calls need a run for accounting, so run-bound access is the only consumption path; fitness row unchanged.
- [x] 6.3 `adr/0017-addendum-model-registry.md` (accepted): middleware failure policy, resolution order, secret handling (hygiene not boundary); no full ADR — the session format does not change.

## 7. Verify, review, archive

- [ ] 7.1 `scripts/verify.sh` green (both cultures); `openspec validate --all --strict` green.
- [ ] 7.2 Adversarial review; fix findings; focused re-review.
- [ ] 7.3 Archive; push; PR.
