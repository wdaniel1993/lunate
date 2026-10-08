# Tasks

## 1. Sample (TDD, red-first)

- [x] 1.1 `MemoryProviderExtension/` per design: manifest, settings schema, `MemoryStore` service, two handlers (context-building capture + injection, turn-ended commit + audit entry), README
- [x] 1.2 `MemoryProviderExtension.Tests/` via the kit: the five cases in design.md; fixture(s) recorded (temporary recorder, not committed; fixture committed + byte round-trip)
- [x] 1.3 Both projects in the solution; layering edges in the gate

## 2. Close

- [x] 2.1 `dotnet csharpier format .`; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-sample-memory-provider --type change --strict`; self-review; commit per group
