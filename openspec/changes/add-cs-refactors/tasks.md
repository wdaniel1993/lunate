# Tasks

## 1. Backend (TDD, red-first)

- [x] 1.1 `ReferencesResult`/`OutlineResult`/`RenamePlanResult` + entries per design; contract methods on `ICSharpBackend`; PublicAPI entries
- [x] 1.2 `FindReferencesAsync` (resolution reuse, candidates, metadata message, cap, ordering)
- [x] 1.3 `OutlineAsync` (syntax-only, no-load path, missing file, syntax-error file)
- [x] 1.4 `PlanRenameAsync` (forked solution, identifier validation, plan shape, disk-untouched invariant)
- [x] 1.5 Fixture additions (duplicate member name, referenced symbol, mixed/nested file, rename target); tests per design incl. cross-project references + determinism

## 2. Tools

- [ ] 2.1 `cs_find_references`, `cs_outline`, `cs_rename` per design (schemas, descriptions, read-only, text + Details, lazy backend)
- [ ] 2.2 Tool tests; lazy-loading assertions extended to all five tools

## 3. Close

- [ ] 3.1 Eval note: rename comparison row recorded for T-35
- [ ] 3.2 `dotnet csharpier format .`; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-cs-refactors --type change --strict`; self-review; commit per group
