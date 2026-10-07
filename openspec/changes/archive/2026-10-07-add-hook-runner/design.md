# Design: add-hook-runner

## Context

The 13-row hook catalogue in `docs/spec/extensibility.md` is the detail spec; ADR-0017 principle 2 pins declared semantics, deterministic order and defined failure policies. T-36 delivered the contract and loader. This change delivers the hook contract, the runner, and wiring wherever a producer exists today. No new packages; no golden changes; no new ADR.

## Hook contract (abstractions, semver-minor)

Typed handler interfaces per catalogue hook; payloads and results are DTO records that round-trip through `System.Text.Json` (one conformance test over all of them). In-process calls pass the DTOs directly; the out-of-process host (T-52) will serialize the same DTOs — the JSON rule is about serializability, not the in-process call shape.

```csharp
public interface IHookHandler { int Priority { get; } }                    // marker + ordering
public interface IProjectTrustHandler : IHookHandler { ValueTask<ProjectTrustResult> HandleAsync(ProjectTrustPayload p, CancellationToken ct); }
public interface ISessionStartedHandler : IHookHandler { ValueTask HandleAsync(SessionStartedPayload p, CancellationToken ct); }
public interface ISessionEndingHandler : IHookHandler { ValueTask HandleAsync(SessionEndingPayload p, CancellationToken ct); }
public interface IInputReceivedHandler : IHookHandler { ValueTask<InputReceivedResult> HandleAsync(InputReceivedPayload p, CancellationToken ct); }
public interface IRunStartingHandler : IHookHandler { ValueTask<RunStartingResult> HandleAsync(RunStartingPayload p, CancellationToken ct); }
public interface IContextBuildingHandler : IHookHandler { ValueTask<ContextBuildingResult> HandleAsync(ContextBuildingPayload p, CancellationToken ct); }
public interface IProviderStreamEventHandler : IHookHandler { ValueTask ObserveAsync(ProviderStreamEventPayload p, CancellationToken ct); }
public interface IMessageCompletedHandler : IHookHandler { ValueTask<MessageCompletedResult> HandleAsync(MessageCompletedPayload p, CancellationToken ct); }
public interface IToolCallingHandler : IHookHandler { ValueTask<ToolCallingResult> HandleAsync(ToolCallingPayload p, CancellationToken ct); }
public interface IToolResultReadyHandler : IHookHandler { ValueTask<ToolResultReadyResult> HandleAsync(ToolResultReadyPayload p, CancellationToken ct); }
public interface ITurnEndedHandler : IHookHandler { ValueTask<TurnEndedResult> HandleAsync(TurnEndedPayload p, CancellationToken ct); }
public interface IRunSettledHandler : IHookHandler { ValueTask ObserveAsync(RunSettledPayload p, CancellationToken ct); }
public interface ICompactingHandler : IHookHandler { ValueTask<CompactingResult> HandleAsync(CompactingPayload p, CancellationToken ct); }
public interface IModelChangedHandler : IHookHandler { ValueTask ObserveAsync(ModelChangedPayload p, CancellationToken ct); }
public interface IToolsChangedHandler : IHookHandler { ValueTask ObserveAsync(ToolsChangedPayload p, CancellationToken ct); }
```

Results are closed unions as records: e.g. `ToolCallingResult` = `Proceed(JsonElement? Arguments)` | `Block(string Reason)`; `MessageCompletedResult` = `Keep` | `Replace(string Text)`; `InputReceivedResult` = `PassThrough` | `Transform(string Text)` | `Consume`; `TurnEndedResult` = `None` | `TurnEnded(entries, requestContinuation)`; `CompactingResult` = `UseDefault` | `Provide(string Summary)`; `ProjectTrustResult` = `Allow` | `Deny(string Reason)`; `RunStartingResult` = section ops + active-tool selection; `ContextBuildingResult` = added messages (source-tagged); `ToolResultReadyResult` = text transform + attached JSON data.

Registration on the context (registration only — no processes/sockets/timers at registration time):

```csharp
public interface IExtensionContext { /* T-36 members */ void Register(IHookHandler handler); }
```

`ExtensionApi.Current` moves to `1.1.0` (semver-minor addition); T-36 extensions remain compatible.

## Runner (host)

- `HookRunner` collects handlers per hook kind across all loaded extensions; order = **priority descending with a stable extension load-order tie-break** (higher priority first; ties keep load order; documented).
- Semantics enforcement per class:
  - **observe**: all handlers run; results ignored; failures reported.
  - **transform**: chained in order — each handler sees the previous output; the final value is used.
  - **replace**: chained like transform (each replacer sees the current text); final value used. Only one replacement is persisted (MessageCompleted).
  - **block**: first `Block` wins and stops the chain (ToolCalling). Handlers before it may have mutated arguments; the final arguments are what the user approves.
- Failure policy per hook (the catalogue table, verbatim): report / skip / keep original / **fail-safe block** (ToolCalling — a throwing or timing-out handler blocks) / deny (ProjectTrust) / fall back (Compacting). A handler exception or timeout applies the policy and is logged through the host sink; it never crashes the run.
- Per-handler timeout: `HookRunnerOptions` (default 5 s; `ProviderStreamEvent` 100 ms fast path; both configurable). Timeout = handler failure for policy purposes.
- Continuation caps: `TurnEnded` continuation requests are counted per run; beyond `MaxContinuations` (default 3) the request is ignored and logged.
- The runner is pure dispatch: it knows no agent types; the adapter (below) maps.

## Wiring (producers that exist today)

- The **harness gains hook points without knowing extensions**: `Lunate.Agent` defines small seam interfaces (Agent-native records, no-op default) invoked when configured: run lifecycle (`RunStarting`, `RunSettled`), context building, provider stream events, message completed, tool calling (before approval — the user approves the final arguments), tool result ready, turn ended. `Lunate.Extensibility` references `Lunate.Agent` (layering gate learns the edge) and implements an **adapter** mapping Agent records ↔ hook DTOs, over the runner.
- **Loader lifecycle**: the loader fires `SessionStarted` (safe to run more than once) at first use and idempotent `SessionEnding` on shutdown.
- **ProjectTrust**: global-extension handlers participate in the loader's trust flow (deny → refused load); handler failure = deny.
- **Contract-only**: `InputReceived` (lands with the input layer/TUI) and `Compacting` (lands with compaction) — documented as unwired with their future producers named; their DTOs and policy tests exist.

## Context tagging

Extension-added context (ContextBuilding, RunStarting sections) is tagged with its source extension id; a per-extension token budget (`HookRunnerOptions.ContextBudgetPerExtension`, default 2,000 tokens estimated) is enforced at merge time — over-budget additions are dropped and logged. The meter/`/context` surfaces land with the TUI cards; the tagging and enforcement land here.

## Testing strategy

- One test per hook for its **semantics class** (order, chaining, block-first, replace-final) and its **failure policy** (throw and timeout paths) — the card's done-gate.
- Determinism: load-order/priority ordering is stable across runs (shuffled registration).
- Conformance: every DTO round-trips through `System.Text.Json` (unknown fields ignored; enums as strings).
- Adapter tests: a test extension with handlers for the wired hooks runs against a harness with a fake chat client (no network).
- Both cultures (suite runs under de-AT).

## Out of scope

Tool registration (T-38), services (T-39), UI/`/context` meter (T-40s), MCP, out-of-process host (T-52), compaction and input layers themselves.
