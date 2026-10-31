# ADR review manifest — extension-contract-primitives

Review completed for this change: 2026-10-29, unattended implementation run (issue #292).

## In-force ADR context reviewed

- **ADR-0017** (`adr/0017-extensibility-architecture.md`) with its **addendum, accepted 2026-10-29**
  — carries every durable decision this change implements: the built-in provenance rule and ordering
  (built-ins load before global and project descriptors), the per-workspace service contract shape and
  lifecycle, the host-evaluated closed condition set, the extension-declared stable alias mechanism
  (option B over a reserved namespace) and the banned-plumbing rule (no `FunctionInvokingChatClient`,
  no `AIFunctionFactory`).
- **ADR-0021** (`adr/0021-per-run-workspaces.md`) — the registry cap default (2), pin/eviction and
  shutdown semantics this change's generic registry generalizes, and its amendment stating extensions
  previously had no workspace-service API.
- **ADR-0012** (`adr/0012-tool-contract.md`) — tools stay `ITool`, executed by the loop.
- **ADR-0006** (`adr/0006-roslyn-placement.md`) — Roslyn's implementation placement, unchanged in this
  change; the migration onto the contract follows in #293.

No supersession edges exist in `adr/`; the repo extends ADRs by numbered addenda, which ADR-0017
already carries for this contract. Highest sequence in use: 0022.

## Decision

No major durable architectural decision is introduced by this change, and no new repository-level ADR
file is created. The per-implementation choices — the rule/evaluator types living in
`Lunate.Extensibility.Abstractions`, the alias handed to the registry through the internal
`ExtensionContext`, the workspace-service registry as a new file in `Lunate.Extensibility` (the core
`WorkspaceServices` stays until #293), and `ExtensionHostOptions.ReservedToolNames` mirroring
`ReservedCommands` — are tactical, documented in this change's `design.md` §1, and bounded by the
contract decisions already recorded in the ADR-0017 addendum.
