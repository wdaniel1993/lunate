## Why

The built-in extension, workspace-service, activation and alias primitives are now implemented, but
the C# tools still bypass them through `Lunate.Coding`. Migrating the flagship now proves the contract
against a real consumer while keeping the existing `cs_*` surface and its measured resource budgets.

## What Changes

- Serve `Lunate.Roslyn` as the host-supplied `roslyn` built-in, with its per-workspace service
  registered and acquired through `IExtensionContext`.
- Declare the existing `.sln`, `.slnx` and `.csproj` root activation rule in the built-in manifest;
  use the host `ToolActivationEvaluator` once, replacing (not duplicating) the `HarnessFactory` gate.
- Preserve all five model-facing names, `ToolResult` text and `IsError` behavior, and startup/memory
  budgets, demonstrated against a recorded pre-migration baseline.
- Remove `HarnessFactory.AddCSharpTools`, the `Lunate.Coding` → `Lunate.Roslyn` project reference and
  its `LayeringChecker` allowance; migrate the separate `lunate mcp roslyn` path onto the same
  built-in/evaluator so no direct Roslyn reference remains.
- Update the C# tool guidance and architecture map, including `/extensions` reporting and resume /
  rollback behavior for sessions created before migration.

This change is design and specification only; implementation and measurements are follow-up work.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `extensions`: define the Roslyn built-in, its manifest-driven service and activation, compatibility
  guarantees, status reporting and session migration behavior.

### Unchanged contracts

The five C# tool requirements in `agent-tools` remain authoritative and unchanged; this migration
must prove parity against their current behavior rather than redefine it.

## Impact

- Future implementation: `Lunate.Coding`, `Lunate.Roslyn`, the proposed `Lunate.Roslyn.BuiltIn`
  adapter, `Lunate.Coding.Tests/LayeringChecker.cs`, the `lunate mcp roslyn` path, and
  migration/budget tests.
- Documentation: `docs/guide.md`, `docs/architecture.md` and `docs/mcp-roslyn.md`.
- No session-format, model-facing tool-name, public API, package or production-code change is part of
  this proposal PR. The precise composition boundary is an architectural decision proposed for
  maintainer sign-off in `adr.md`.
