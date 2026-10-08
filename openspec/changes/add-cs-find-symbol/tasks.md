# Tasks

## 1. Backend (TDD, red-first)

- [x] 1.1 `SymbolSearchResult`/`SymbolMatch`/`SymbolSearchStatus` per design; `FindSymbolAsync` on `ICSharpBackend` + in-process implementation (resolution, dotted paths, ordering, bounded matches, metadata marking); PublicAPI entries
- [x] 1.2 Fixture additions (symbols per design, both fixtures)
- [x] 1.3 Tests: all cases in design.md incl. signature snapshot, statuses, ordering stability

## 2. Tool

- [ ] 2.1 `cs_find_symbol` per design (schema, description, read-only, text + Details, lazy backend, freshness, status texts)
- [ ] 2.2 Tool-level tests (resolved result, not-found hint, status mapping; lazy-loading unaffected)

## 3. Close

- [ ] 3.1 `dotnet csharpier format .`; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-cs-find-symbol --type change --strict`; self-review; commit per group
