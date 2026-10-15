# Design

## D1 — Emission point: `SwitchToModel`, the single chokepoint

Both user paths for changing the active model converge on `SwitchToModel` (`InteractiveSession.Commands.cs`): `/model <id>` → `SwitchModel(id)` and the picker confirm → `SwitchModel(value)`. Emitting there guarantees one event per actual change, in both paths, with no duplication. No emission when the id is unknown (no change happened — the notice path returns before `SwitchToModel`). Not emitted at session start: the hook is "after a change", and the start model is reported by `SessionStarted`'s context (working directory/identity) and the footer; a change-only contract keeps the event meaningful.

Mechanics: the emission is awaited. `SwitchToModel`/`SwitchModel`/`ConfirmPicker`/`HandlePickerKey` become `ValueTask`-returning (all private), which is cheap because the key path is already async (`HandleKeyAsync` at `InteractiveSession.cs:600` awaits them; the `/model` command case is inside async `DispatchCommandAsync`). The runner resolves exactly like the input pipeline's: `_options.Hooks ?? _extensions?.HookRunner`, null-safe (no runner → no emission, matching "with no hook runner configured, input passes through unchanged"). The command/key cancellation token is threaded through, consistent with `RunInputReceivedAsync`'s call.

## D2 — Payload: empty run id, per the out-of-run convention

`ModelChangedPayload(string RunId, string Model)`. The switch is refused while a turn runs (`RefusedWhileBusy`), so it always happens **outside a run** — `RunId = string.Empty`, matching the documented `ToolsChanged` convention ("Add and remove happen outside a run, so the payload carries an empty run id", `ExtensionLoader.NotifyToolsChangedAsync`). `Model` = the new catalog model id. Failure semantics: `RunModelChangedAsync` follows the observe policy (handler failures reported, never thrown into the switch) — nothing to add.

## D3 — Why not "document as unwired"

The alternative in the card (narrow the doc to "contract-only, unwired") was rejected: the emission point is a single chokepoint, the payload/handler/runner all exist and are conformance-tested, the catalogue and doc promise "observe after change", and T-42 set the precedent (wire the hook when its surface exists — `InputReceived`). Documenting it as unwired would make the catalogue honest but permanently incomplete for a hook whose only missing piece was one awaited call. `ModelRouted` (T-49, run-level, per-call resolution) is *not* a substitute: it fires during runs for routed calls, not on the session-level switch.

## D4 — Spec sync scope

MODIFIED "Hook wiring" in `openspec/specs/extensions/spec.md`: add `InputReceived` (input pipeline; transformed text proceeds / consumed input reported — before anything starts or queues) and `ToolsChanged` (tool-registry composition changes on load/unload; empty run id) to the wired list; add `ModelChanged` (session model switch; empty run id); retire the stale "Hooks without producers yet (`InputReceived`) SHALL be contract-complete and documented as unwired" sentence in favour of "Every catalogue hook SHALL have a producer" (verified against the T-53 audit + spot-checks); preserve the harness-agnostic and child-run sentences and all four existing scenarios verbatim; add one scenario for the new emission. `docs/spec/extensibility.md`'s catalogue table needs no edit (it states the contract, not the wiring status).
