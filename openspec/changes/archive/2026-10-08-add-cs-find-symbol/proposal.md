# add-cs-find-symbol

## Why

Card T-26 (deps T-25): the second agent-shaped C# tool. Guide: "`cs_find_symbol` — Definition location and signature for a type or member name — navigates large solutions without grepping". Phase 5 ships it together with `cs_diagnostics` because name-based lookup is half the .NET-edge claim. Done-gate: finds definitions in the fixture solutions.

## What Changes

- **Backend**: `ICSharpBackend` gains `FindSymbolAsync(string name, CancellationToken ct)` → `SymbolSearchResult` (matches: kind, name, container, file, 1-based line/column, signature; bounded + truncation; load statuses carried through). Name-based — position lookup stays in the backend (ADR-0014).
- **Tool `cs_find_symbol`**: `name` (required); read-only; loads the backend lazily like `cs_diagnostics`; before searching, the same freshness sync; load failures map to the same actionable statuses.
- Fixture solutions gain a handful of well-known symbols (types, overloads, a partial type) for deterministic tests.
- Spec delta: `agent-tools` gains the symbol-lookup requirement + scenarios.

## Impact

- `Lunate.Roslyn` only (contract + backend method + tool; PublicAPI entries). No new packages; no new ADR; no layering changes.
