# Design — service-composition production tests

## Gap 1: background-service lifecycle (production path)

Existing coverage before this change: the kit host (`Lunate.Extensibility.Testing.Tests`) covers start/stop/idempotency; `ExtensionSessionTests` covers the cancellation-mid-compose and inspect-mode no-start paths at session level. Missing: the **happy path through a real composed run** — compose → service starts once → session ends → service stops once — the exact scenario the `extensions` spec states ("A service starts and stops with the session").

Decision: prove it through a **print run** (`Cli` path with `ExtensionSeams` + the hello fixture), asserting the service lines exactly once each plus the fixture's own `hello lookups: bus=True, queue=True, service=True` line, which additionally proves the composed registry exposes the core handles and the extension's service at factory time. The print run is the lightest composed path that exercises the full load/start/end sequence; interactive/ACP paths share the same `ExtensionSession` lifecycle.

## Gap 2: cross-extension service consumption (production path)

Existing coverage: `ServiceRegistryTests` (unit) and the loader-level hello lookups (`ExtensionLoaderFileChangeTests`). Missing: two extensions in one composed session where one consumes the other's registered service by name — the feature the spec's shared-namespace wording promises ("duplicate names SHALL be refused with an error naming both extensions" only makes sense across extensions).

Decision: a new minimal fixture `ServiceConsumerExtension` that looks up `ext/hello/greeter` (present) and `ext/absent/greeter` (absent control) through `IExtensionContext.TryGetService` and logs the outcome; the test installs it next to the hello fixture and asserts the log line through the composed run.

**Consumption point — run start, not session start.** The lookup happens in an `IRunStartingHandler`, not `ISessionStartedHandler`. Reason discovered while building: `SessionStarted` fires during the *first* extension's load (`ExtensionLoader.Load.cs`), so a session-start lookup would be load-order-dependent (the consumer would only see providers that sort before it) — the first attempt reproduced exactly that (`provider=False` with `consumer` sorting before `hello`). Run start fires after every extension has loaded, is deterministic, and matches the docs' "start long-lived resources in `SessionStarted` **or on first use**" pattern for consumption. The `SessionStarted` firing semantics themselves are a separate maintainer decision (filed with a repro); this test must not pin that behaviour either way.

## Gap 3 (audit item): four older samples kit-only — narrowing decision

permission-gate, memory-provider, lsp-diagnostics, code-mode are exercised by their own test projects through the kit (`ExtensionTestHost`) and their hooks through `AgentHookAdapterTests` ("every wired hook end-to-end"). The kit is a **shipped, first-class surface** — the spec requires production sessions and the test kit alike to register the core handles — so kit-level sample tests are legitimate coverage of a sample's own logic; the production **machinery** the samples rely on (hook wiring, service lifecycle, registry consumption) is covered generically by the existing `ExtensionCompositionEndToEndTests` and the two new tests here. Decision: no additional per-sample production tests; newer samples' extra production-mode proofs stay as their own targeted claims (TUI rendering, MCP spawning, model routing). If a future change gives an older sample a user-visible claim beyond its hooks, it should get its own production proof then.

## Test plumbing

- New fixture project `tests/TestExtensions/ServiceConsumerExtension` (mirrors `SkillSourceExtension`: abstractions-only reference, one public factory, copy-to-output wiring in `Lunate.Coding.Tests.csproj`, `LayeringChecker` entries).
- Both tests use `PrintModeTestSupport` + `ScriptedChatClient`; no new packages; no production code touched.
