# Design: add-cs-refactors

## Context

Builds on T-25/T-26 machinery (backend, freshness, lazy load, status vocabulary, resolution). ADR-0014: name-based ops; the backend never mutates; rename = plan only. No new packages; no ADR.

## Contract (pinned signatures)

```csharp
ValueTask<ReferencesResult> FindReferencesAsync(string name, CancellationToken ct);
ValueTask<OutlineResult> OutlineAsync(string file, CancellationToken ct);
ValueTask<RenamePlanResult> PlanRenameAsync(string name, string newName, CancellationToken ct);
```

### FindReferencesAsync
- `Status` = T-26 vocabulary. `Resolved` (SymbolMatch? the resolved symbol's summary: kind/name/container/declaration location) + `References` (capped 200 + `TotalReferenceCount` + `Truncated`; each: file relative-under-root, 1-based line/column, no snippet — cheap) + `Candidates` when ambiguous.
- Resolution: exactly T-26's (simple name = unique match required; multiple matches → empty references + candidates + hint "use a dotted path"; dotted path → exact). Metadata/BCL targets: message "metadata symbol — no source references"; never an error. References = usage locations (declaration excluded; the declaration location is reported once in `Resolved`). Freshness sync first. Partial loads OK.

### OutlineAsync
- Input: file (relative to worktree root or absolute inside it). Syntax-level only: `CSharpSyntaxTree.ParseText` of the on-disk text — **no solution load required** (works when NoSolution/NoSdk). Items (capped 200 + total + `Truncated`): kind (T-26 vocabulary), container (dotted path), name, signature (declaration header, body-free — e.g. `public int Add(int a, int b)`, `class Calculator`), 1-based line. Deterministic source order (as in the file). Missing/unreadable file → actionable message, not an error. Empty file → empty outline + note.

### PlanRenameAsync
- Resolve `name` exactly as find-references (ambiguity → candidates + hint; no plan). Validate `newName` is a legal C# identifier (message with the rule reference otherwise). Compute via `Renamer.RenameSymbolAsync` on a **forked solution** (`Solution.With…` never touches the workspace or disk); per-file plan entries: file (relative), change count / hunk summaries (context line before/after bounded to a few chars? Keep: line + old text → new text for identifier occurrences, capped 500 occurrences + totals + `Truncated`), plus `UnchangedOnDisk: true` semantics by construction. Message: "planned N edits in M files; nothing was changed — apply via edit/write".
- Metadata symbol targets → message, no plan. Partial loads OK.

## Tools

- `cs_find_references`: `{ "name": string, required }`; read-only; text: resolved summary + definition location + up to 10 inline usages; full list in Details.
- `cs_outline`: `{ "file": string, required }`; read-only; text: flattened outline (indent by container depth, up to 40 inline); full list in Details.
- `cs_rename`: `{ "name": string, required, "newName": string, required }`; read-only (plan only!); text: plan summary + up to 10 file lines; details full; the "nothing was changed" sentence always present.
- All three: lazy backend (same construction discipline), read-only annotations, status texts consistent with T-25/T-26.

## Tests

- References: symbol with usages in both fixture projects (cross-project resolution); zero-usage symbol → empty + note; ambiguous simple name → candidates + hint; dotted disambiguation works; metadata target message; cap/truncation unit test; determinism (two runs identical).
- Outline: nested types + members without bodies (assert body text absent from signatures); syntax-error file still outlines; missing file message; no-solution backend still outlines (no load); cap.
- Rename: plan lists both fixture files with expected occurrences; **disk untouched after planning** (hash the fixture tree before/after — the key ADR assertion); invalid newName message; ambiguous name → candidates; metadata target message; plan determinism.
- Both cultures; warm latencies tripwire-asserted (disclosed); lazy-loading tests extended to all five tools.
- Note the rename scenario for the T-35 eval comparison row.

## Out of scope

Applying renames (the edit/approval path does; ADR-0014), semantic outline formatting, snippet capture in references, LSP backend, RoslynBackend.cs split (deferred cleanup).
