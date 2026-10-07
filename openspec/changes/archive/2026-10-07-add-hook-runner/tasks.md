# Tasks

## 1. Hook contract (TDD, red-first)

- [x] 1.1 Payload/result DTOs for all 13 catalogue hooks (records; JSON shapes per design.md); a conformance test round-trips every DTO through `System.Text.Json`
- [x] 1.2 Handler surface per semantics class (observe | transform | replace | block) + registration on `IExtensionContext` (registration only)
- [x] 1.3 Semver-minor versioning note: `ExtensionApi.Current` stays compatible; PublicAPI entries for the new surface

## 2. Runner

- [x] 2.1 Deterministic dispatch: handlers in load order, then optional priority; stable ordering tests
- [x] 2.2 Semantics enforcement per hook class; invalid transitions refused (e.g. a transform hook cannot replace)
- [x] 2.3 Failure policy per hook: report / skip / keep original / fail-safe block / deny / fall back — one test per hook for its declared policy
- [x] 2.4 Per-handler timeout (configurable, default per design); a hanging handler is cut off per policy
- [x] 2.5 Continuation caps: continuations requested at turn boundaries are capped per run (default 3)

## 3. Wiring (producers that exist today)

- [x] 3.1 Loader lifecycle: `SessionStarted` (safe to run more than once) and idempotent `SessionEnding`
- [x] 3.2 Harness: `RunStarting`, `ContextBuilding`, `ProviderStreamEvent`, `MessageCompleted`, `ToolCalling` (before approval, final arguments approved), `ToolResultReady` (composes in order), `TurnEnded`, `RunSettled` — through the adapter; each with a test
- [x] 3.3 `ProjectTrust`: global-extension handlers participate in the loader's trust flow
- [x] 3.4 Contract-only hooks (`InputReceived`, `Compacting`) documented as unwired with their future producers named

## 4. Context tagging and close

- [x] 4.1 Extension-added context tagged with its source + per-extension token budget enforced
- [x] 4.2 `dotnet csharpier format .`; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-hook-runner --type change --strict`; self-review; commit per group
