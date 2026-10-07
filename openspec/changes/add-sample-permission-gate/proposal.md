# add-sample-permission-gate

## Why

Card T-43 (deps T-37; the testing kit T-42 just landed): the first fitness sample. `docs/spec/extensibility.md`: "permission-gate | ToolCalling + annotations → block/confirm" and "A sample that needs a workaround means the core is missing a primitive." Writing the sample surfaced exactly that: the `ToolCalling` payload carries name + arguments but **not the resolved tool's annotations**, so a policy extension cannot decide on `Destructive`/`ReadOnly` before approval without a workaround (name lists). This change adds the missing primitive (a semver-minor contract addition), pins it in the spec, and ships the sample that proves it.

## What Changes

- **Contract (semver-minor → API 1.3.0)**: `ToolCallingPayload` gains the resolved tool's annotations as a string list (the tool-model annotation names, stable order); the agent seam passes the resolved tool's annotations to the adapter. Conformance test extended. No new hooks.
- **Spec delta**: MODIFIED `Hook wiring` requirement — `ToolCalling` carries the tool's annotations so policy handlers can decide before approval; new scenario covering it.
- **Sample `samples/extensions/permission-gate/`**: an extension (abstractions-only) whose `ToolCalling` handler enforces a settings-driven policy — `block` mode blocks calls to annotated-destructive tools (deny-style result, before the approval prompt) with a reason; `confirm` mode lets them fall through to the normal approval flow. Tests via the testing kit: destructive call blocked, read-only call proceeds, confirm mode falls through to the (scripted) approver, settings drive the mode.
- Sample README lists the core capabilities it proves (fitness suite convention).

## Impact

- `Lunate.Extensibility.Abstractions` (1.3.0): payload field + PublicAPI; `Lunate.Extensibility` adapter; `Lunate.Agent` seam record passes annotations.
- New `samples/extensions/permission-gate/` (project + tests) in the solution; layering edges in the gate.
- No new packages; no golden changes; no new ADR (implements ADR-0017 + existing spec text).
