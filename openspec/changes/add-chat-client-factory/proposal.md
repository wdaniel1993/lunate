## Why

Phase 1 opens with the model-access seam: `Lunate.Ai` builds `IChatClient` pipelines and owns the model catalog (guide card T-04, spec Layer 1). Everything above it — recorder/replay, providers, the agent loop — depends on this factory and on the pipeline order the guide and ADR-0003 fix (OpenTelemetry → logging → recorder → provider, outermost first). Building it first keeps T-05/T-06/T-09 from inventing their own model-access paths.

## What Changes

- `Lunate.Ai` gains the guide's Layer 1 types: `ModelInfo`, `IChatClientFactory`, `ModelCatalog`, and the pipeline assembly in the fixed order — OpenTelemetry (opt-in) → logging → recorder slot (filled by T-05) → provider — with replay later replacing recorder + provider so tests still run through telemetry and logging.
- The model catalog is a built-in `models.json` merged with `~/.lunate/models.json` (user overrides by id; adding a model never needs a code change).
- The first provider path is the OpenAI-compatible one via the Microsoft.Extensions.AI OpenAI adapter (Anthropic follows in T-06); the new package pins are listed in the design and come from the guide's tech stack.
- Rules that bind the layer: no `FunctionInvokingChatClient`/`AIFunctionFactory` (ADR-0003), no retries here (they live in the loop, as events), reuse `UseLogging`/`UseOpenTelemetry`/`AIJsonUtilities` per the spec's reuse list.

## Capabilities

### New Capabilities
- `ai-layer`: the factory's pipeline order, the catalog merge contract, and the layer's reuse/forbidden-usage rules.

### Modified Capabilities
- None.

## Impact

- `src/Lunate.Ai` (new public types + `PublicAPI.Unshipped.txt`), `tests/Lunate.Ai.Tests` (merge + pipeline tests), an embedded `models.json` resource.
- No changes to other projects; no packages beyond the guide's tech-stack picks (listed in the design).
