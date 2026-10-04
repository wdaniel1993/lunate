## Context

Spec: guide Layer 1 (`docs/guide.md`, "Layer 1: Lunate.Ai") — this change implements the parts T-04 owns: `ModelInfo`, `IChatClientFactory`, the pipeline order and the model catalog. Recording/replay (T-05), further providers (T-06) and `ProviderErrors` (with the loop's retry work, T-10) are explicitly out of scope.

## Goals / Non-Goals

**Goals:** the seam everything above depends on — a factory that returns a correctly ordered pipeline per model, a catalog that merges built-in and user models without code changes, and tests that pin both.

**Non-Goals:** real network calls in tests (stub provider client); Anthropic (T-06); recorder/replay clients (T-05, but the factory keeps their slot); retries (loop, T-10); settings/auth file loading (T-16 — see Decisions).

## Decisions

- **Types** (per the guide): `ModelInfo(string Id, string Provider, Uri? Endpoint, int ContextWindow, bool SupportsTools)`; `IChatClientFactory.Create(ModelInfo) -> IChatClient`.
- **Pipeline order, outermost first**: OpenTelemetry (opt-in via config/`OTEL_*` env) → logging (`UseLogging`, injected `ILoggerFactory`) → recorder slot (T-05 inserts `RecordingChatClient` when `LUNATE_RECORD=1`; empty in T-04) → provider client. Replay will replace recorder + provider (T-05), keeping telemetry + logging in the test path.
- **Provider construction**: `ModelInfo.Provider` selects the adapter; v1 is the OpenAI-compatible path via `Microsoft.Extensions.AI.OpenAI` (the adapter accepts a custom endpoint, which the home-lab models need). API keys resolve from environment variables for now (documented convention, e.g. `OPENAI_API_KEY`); `~/.lunate/auth.json` + settings merge is T-16. Tests never need keys (stub client).
- **Catalog**: embedded `models.json` (schema version; id, provider, endpoint, contextWindow, supportsTools; a small starter set) + optional `~/.lunate/models.json`. Merge: user entries override built-ins by id; duplicate ids within one file are an error; a missing user file is fine. Adding a model = editing JSON, never code.
- **Testing**: unit tests with a stub `IChatClient` (no network): pipeline order (observable via test doubles), catalog merge (temp HOME), validation errors. Contract tests against real endpoints come with T-06.
- **Packages** (exact pins at implementation; all from the guide's tech stack): `Microsoft.Extensions.AI` + `Microsoft.Extensions.AI.Abstractions` (10.10.x, matching the spikes), `Microsoft.Extensions.AI.OpenAI` (latest 10.10-compatible; fetch from nuget.org), `Microsoft.Extensions.Logging.Abstractions`, `OpenTelemetry.Api` if `UseOpenTelemetry` requires it. No other packages.

## Risks / Trade-offs

- [Adapter churn] → pin exact versions; the adapter stays behind the factory interface.
- [Catalog schema drift] → schema version field + data-driven merge tests.
- [Key handling before T-16] → env-var convention documented; T-16 replaces it with auth.json/settings.

## Migration Plan

Not applicable — additive library work.

## Open Questions

- None blocking.
