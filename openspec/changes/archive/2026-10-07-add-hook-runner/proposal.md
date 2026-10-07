# add-hook-runner

## Why

T-36 gave extensions a contract and a loader; ADR-0017 principle 2 requires every hook to have declared semantics — observe | transform | replace | block — with deterministic order and a defined failure policy, and the spec's 13-row hook catalogue is the detail every later card builds against. This is card T-37: the hook runner (catalogue semantics, order, failure policy, per-handler timeouts), done when every hook has a test for its semantics class and its failure policy.

## What Changes

- **Hook contract** (abstractions, semver-minor): typed payload/result DTOs for all 13 catalogue hooks (JSON DTOs, conformance-tested through `System.Text.Json`), the handler interfaces per semantics class, and the registration surface on `IExtensionContext` (registration only — no processes, sockets or timers at registration time).
- **Hook runner** (host): deterministic dispatch (load order, then optional priority), semantics enforcement per hook class (observe/transform/replace/block), per-handler timeouts, the declared failure policy per hook (report / skip / keep original / fail-safe block / deny / fall back), and per-run continuation caps (default 3).
- **Wiring where producers exist today**: `SessionStarted`/`SessionEnding` (loader lifecycle), `RunStarting`, `ContextBuilding`, `ProviderStreamEvent`, `MessageCompleted`, `ToolCalling` (before the approval prompt), `ToolResultReady`, `TurnEnded`, `RunSettled`, `ProjectTrust` (global-extension handlers in the loader's trust flow). `InputReceived` and `Compacting` are contract-only until their producing layers land (documented).
- **Context tagging**: extension-added context is tagged with its source and has a per-extension token budget (the meter/`/context` surfaces land with the TUI cards; the tagging and budget enforcement land here).
- Spec: `extensions` capability gains the runner requirements (semantics classes, ordering, failure policy, timeouts, continuation caps).

## Impact

- `Lunate.Extensibility.Abstractions`: hook DTOs + handler/registration surface (semver-minor; PublicAPI tracked).
- `Lunate.Extensibility`: the runner (+ tests); `Lunate.Agent` harness gains hook invocation points where producers exist (the harness stays identity-agnostic; integration through a small adapter).
- No new packages; no golden changes; no new ADR (implements accepted ADR-0017).
