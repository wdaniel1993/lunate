# Tasks — sample-model-router

## 1. Contract (`Lunate.Extensibility.Abstractions`)

- [ ] 1.1 `IModelRegistry`, `IModelAccess`, `ExtensionModelProvider`, `ExtensionModelMiddleware`, `ModelMiddlewareSlot`, `VirtualModelRegistration`, `ModelRouteContext` per D1 (XML docs; namespacing rules stated); `IExtensionContext` gains `RegisterModelProvider`/`RegisterModelMiddleware`/`RegisterVirtualModel`; API 1.9 → 1.10; PublicAPI.Unshipped; ExtensionApiTests updated.
- [ ] 1.2 Registration validation: id shape + namespacing, duplicates refused naming both extensions, caps (8/8/16); units.

## 2. Live registry (`Lunate.Extensibility`)

- [ ] 2.1 `ModelRegistry`: resolution order (virtual → programmatic provider → declared provider → catalog → teaching error), namespaced ids, `Resolve` composition (middleware chain per D2 + router clients), first-resolve backend validation, factory-exception teaching errors.
- [ ] 2.2 Declared-provider instantiation (D4): lazy per model id, host-side secret read, `ChatClientFactory` explicit-key overload (`Lunate.Ai`), defaults, missing-secret teaching error.
- [ ] 2.3 Units: resolution order incl. catalog fallback; middleware order; per-request routing with one client; unknown backend; missing secret; factory throws.

## 3. Run-bound access (`Lunate.Agent`)

- [ ] 3.1 `ToolContext.Models` stamped by the harness (`AgentHarnessOptions.ModelRegistry`); null outside a run / without a registry.
- [ ] 3.2 Usage-charging wrapper: nested calls emit `UsageUpdated(owningRunId)` + count into `_tokenBudgetUsed` + inherited tree accounting — shared with the run's own accounting path (D5).
- [ ] 3.3 Units: `For` outside a run → null; usage charged (event + budget + tree) with a scripted registry/harness; resolution failure teaching error through the seam.

## 4. Wiring (`Lunate.Coding`)

- [ ] 4.1 `ExtensionSession`/`HarnessFactory`: registry → run options; `/context` (or docs) may name the extension models — keep minimal, note in docs.
- [ ] 4.2 Integration: an extension-registered provider resolvable from a real composed session (focused test).

## 5. Sample (`samples/extensions/model-router`)

- [ ] 5.1 Extension + manifest (declared provider `router-local`, endpoint + `router-key` + ids fast/deep), virtual model `auto` (selector + `deepThresholdChars` setting), counting middleware, tool `router__ask` (read-only), README (what it proves, honest limits).
- [ ] 5.2 Tests: routing per request (short → fast, long → deep, same client); usage roll-up (events + budget) via the real composition; middleware counts backend calls only; teaching errors (unknown model, unknown backend, missing secret); declared-provider instantiation against a local fake OpenAI endpoint (HttpListener): `Authorization` header carries the secret, the extension surface never shows the value.
- [ ] 5.3 Solution entries + AssemblyInfo parallelization pattern.

## 6. Spec + docs

- [ ] 6.1 `extensions` spec deltas: MODIFIED "Model provider registry" (deferral replaced by the real contract; original scenarios preserved + instantiation scenario) + ADDED "Model registry: middleware and virtual models" + ADDED "Nested model calls charge the owning run".
- [ ] 6.2 `docs/spec/extensibility.md`: `core/models` named in the core-handles paragraph; the `IModelRegistry` promise aligned with production; fitness row unchanged.

## 7. Verify, review, archive

- [ ] 7.1 `scripts/verify.sh` green (both cultures); `openspec validate --all --strict` green.
- [ ] 7.2 Adversarial review; fix findings; focused re-review.
- [ ] 7.3 Archive; push; PR.
