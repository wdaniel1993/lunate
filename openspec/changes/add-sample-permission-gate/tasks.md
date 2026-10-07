# Tasks

## 1. Contract addition (TDD, red-first)

- [x] 1.1 `ToolCallingPayload` gains annotations (lowercase kebab wire names in declaration order — `read-only`, `destructive`, `idempotent`, `open-world`; one new mapping helper in `Lunate.Agent` next to `ToolAnnotations`, reused by the adapter; empty when none); conformance test extended; PublicAPI entries; API 1.3.0 with pinned tests updated
- [x] 1.2 Agent seam + adapter pass the resolved tool's annotations through (behavior unchanged when hooks are unconfigured — test)
- [x] 1.3 Spec delta restated (Hook wiring + new scenario); `openspec validate --strict`

## 2. Sample

- [ ] 2.1 `samples/extensions/permission-gate/PermissionGateExtension/` per design (manifest, settings schema, factory, ToolCalling policy, logging)
- [ ] 2.2 `PermissionGateExtension.Tests/` via the kit: the five cases in design.md; fixture stream for destructive + read-only calls (recorded, byte round-trip)
- [ ] 2.3 README (capabilities proven); both projects in the solution; layering edges in the gate

## 3. Close

- [ ] 3.1 `dotnet csharpier format .`; `bash scripts/verify.sh` green (incl. de-AT); self-review; commit per group
