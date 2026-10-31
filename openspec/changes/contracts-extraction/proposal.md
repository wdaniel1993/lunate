## Why

`Lunate.Extensibility.Abstractions` currently depends on `Lunate.Agent` for the tool and event
contracts it exposes, making the extension boundary depend on its implementation. The accepted
ADR-0002 addendum establishes `Lunate.Contracts` below both assemblies so the extension API can be
published without reversing the core-to-extension dependency.

## What Changes

- Add `Lunate.Contracts` for shared tool contracts, declarations, annotations and the complete
  `AgentEvent` hierarchy; retain forwarding types in their former assemblies for compatibility.
- Make Agent and Extensibility.Abstractions depend on Contracts, and forbid references between
  Agent and Extensibility.Abstractions in either direction.
- Update extension loading, single-file builds, packaging, API shipping, versioning and layering
  checks so Contracts is part of the host-owned extension contract.
- Amend the extension-contract requirement and align the pending decision-layer change with the
  accepted ADR-0002 addendum: decision request/result types belong in Contracts and
  `IDecisionService` remains in Abstractions.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `extensions`: extensions compile against both contract assemblies; shared types live in Contracts,
  which Abstractions references without depending on Agent, and both contract assemblies are
  host-owned and versioned together.

## Impact

Affected projects include `Lunate.Contracts`, `Lunate.Agent`, `Lunate.Extensibility.Abstractions`,
`Lunate.Extensibility`, and extension/host test projects. Build and package manifests, API shipping,
single-file compilation, project-layering checks, and extension load behavior are updated. No new
package is required; `ExtensionApi.Current` advances from 1.13.0 to 1.14.0.
