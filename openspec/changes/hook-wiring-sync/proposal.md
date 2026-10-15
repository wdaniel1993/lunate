# Hook wiring: emit ModelChanged at the session model switch; sync the wiring spec

## Why

Card `t_7dd717e9` (gap from the spec-vs-production audit, `t_298479fe`): the doc hook catalogue promises `ModelChanged` ("after change | observe") but **no producer exists anywhere** — `ModelChangedPayload` + `IModelChangedHandler` + `HookRunner.RunModelChangedAsync` are contract-complete and test-covered, yet nothing fires them. Meanwhile the spec's Hook-wiring paragraph is stale in the other direction: it lists `InputReceived` as "without producers yet" (wired since T-42 through the input pipeline) and omits `ToolsChanged` from the wiring list (wired on load/unload since the tool-registry work). The spec is the source of truth; it must match production, and `ModelChanged` must either gain its producer or be honestly documented as unwired.

## What Changes

- **`ModelChanged` gains its producer.** The session model switch — `/model <id>` and the picker, which share `SwitchToModel` — emits `ModelChanged` with the new model id and an **empty run id** (the switch happens between runs; `ToolsChanged` sets the out-of-run convention). Awaited after the state change; handler failures follow the observe policy (reported, never break the switch). `ModelRouted` stays untouched — it is the run-level routing event (T-49), a different layer.
- **The Hook-wiring spec paragraph is corrected** to production reality: `InputReceived` and `ToolsChanged` join the wired list with their producer notes; the "without producers yet" sentence retires (every catalogue hook has a producer); one new scenario pins the `ModelChanged` emission.
- **The doc catalogue needs no change** — its row (`ModelChanged / ToolsChanged | after change | observe | report`) becomes true as written once the emission exists.

## Impact

- **Behaviour change: one new hook emission** (session model switch) → sign-off card `t_798193d8` (maintainer decision, per the card's "needs a sign-off at draft time if wired").
- Spec delta: **extensions** (MODIFIED "Hook wiring": wired list + retired unwired sentence + one scenario; the four existing scenarios preserved verbatim).
- Public API: none (no new types; `SwitchToModel` and friends are private).
- No new packages; no ADR (the catalogue already promised the hook — this completes it; the alternative "document as unwired" is recorded in the design).
