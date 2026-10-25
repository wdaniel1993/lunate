## 1. The rule

- [x] 1.1 `ReferencePackRules.NearestBelow` (`nearest-below-runtime`), documented as the last resort.
- [x] 1.2 `ReferencePackRule.Select` falls back to the highest published pack of the same major/minor below the runtime, only when nothing at or above it exists.
- [x] 1.3 Rule tests: the last resort is chosen with its rule name; a pack at or above still wins; another minor is never a last resort.
- [x] 1.4 Restore-engine test: a runtime ahead of the feed restores `10.0.4` for runtime `10.0.5` and records `nearest-below-runtime`; a feed with no pack of that minor still throws naming the runtime.

## 2. Gate

- [ ] 2.1 `openspec validate reference-pack-ahead-of-feed --strict` green, `openspec validate --all --strict` green.
- [ ] 2.2 `dotnet csharpier check .` clean; `tests/Lunate.Extensibility.Restore.Tests` and `tests/Lunate.Extensibility.Compile.Tests` green.
- [ ] 2.3 `scripts/verify.sh` green on both culture passes.
- [ ] 2.4 All four CI legs green — the compile tests that failed on every leg are the ones this change exists for.
