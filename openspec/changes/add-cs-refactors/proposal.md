# add-cs-refactors

## Why

Card T-31 (deps T-25): the remaining agent-shaped C# tools — `cs_find_references`, `cs_outline`, `cs_rename`. Guide: "Safe changes to public APIs", "Cheap context for big files", "Refactoring that grep-and-replace gets wrong". Done-gate: fixture tests; eval comparison row noted for T-35.

ADR-0014 governs: name-based operations; position lookup in the backend; **rename is planned, never applied by the backend** — `PlanRenameAsync` returns proposed edits as diffs and the existing edit/approval path applies them (the guide's "applies a solution-wide rename" is realized by that path, not by magic writes).

## What Changes

- **Backend**: `ICSharpBackend` gains `FindReferencesAsync(name, ct)`, `OutlineAsync(file, ct)`, `PlanRenameAsync(name, newName, ct)` with pinned result shapes. References: usages of an exactly-resolved symbol (dotted path disambiguates; ambiguous simple names list candidates, never guess). Outline: syntax-level types + member signatures of one file, no bodies — works without a loaded solution. Rename: per-file edit plan (bounded diffs/summaries), **nothing applied, disk untouched**.
- **Tools**: three read-only-by-contract `ITool`s with the same lazy backend, freshness and status machinery as T-25/T-26; `cs_rename` clearly reports "nothing was changed — apply via edit/write".
- Fixtures: a duplicate member name across two types (ambiguity), an explicitly referenced symbol (references), a nested/mixed file (outline), a rename target with references in both fixtures.
- Spec delta: `agent-tools` gains the three requirements + scenarios.

## Impact

- `Lunate.Roslyn` only (contract + backend + tools; PublicAPI). No new packages; no new ADR; no layering changes.
