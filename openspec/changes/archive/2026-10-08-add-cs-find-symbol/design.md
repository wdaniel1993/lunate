# Design: add-cs-find-symbol

## Context

Builds entirely on T-25's machinery (backend, freshness, lazy load, statuses). ADR-0014: name-based operations; position lookup in the backend; the backend never mutates. No new packages; no ADR.

## Contract (pinned signatures)

```csharp
ValueTask<SymbolSearchResult> FindSymbolAsync(string name, CancellationToken ct);

public sealed record SymbolSearchResult(
    SymbolSearchStatus Status,          // Loaded | Partial | RestoreRequired | NoSdk | NoSolution
    string Message,
    IReadOnlyList<SymbolMatch> Matches, // bounded (50) + TotalMatchCount + Truncated
    int TotalMatchCount
);

public sealed record SymbolMatch(
    string Kind,            // "namespace" | "class" | "struct" | "interface" | "enum" | "delegate" | "method" | "constructor" | "property" | "field" | "event" (lowercase words)
    string Name,            // simple name
    string? Container,      // dotted container path (namespace/type chain), null when none
    string? File,           // relative when under the root; null for metadata-only symbols
    int Line,               // 1-based; 0 when no source location
    int Column,             // 1-based; 0 when no source location
    string Signature,       // display string, format pinned below
    bool FromMetadata       // reference-assembly symbol without source
);
```

Kind mapping: delegates → `delegate`, constructors → `constructor`, records → `class` (records are classes).

## Search semantics (pinned; deterministic)

- Input: a simple name (`Calculator`) or a dotted container path (`CalculatorLib.Calculator`). Trimmed; empty/whitespace → `Loaded` + empty matches + message asking for a name (never an error status).
- Resolution: per loaded project compilation `GetSymbolsWithName(lastSegment, SymbolFilter.Type | SymbolFilter.Member | SymbolFilter.Namespace)`; when the input has dots, keep symbols whose dotted chain (namespace + containing types) equals the input prefix (ordinal, case-sensitive — C# is case-sensitive; a case-mismatch returns empty with a hint).
- Source definitions only for declaration sites: map via `DeclaringSyntaxReferences`; symbols with source location under the root get relative file + 1-based line/column. Metadata-only symbols: `FromMetadata = true`, no file, still listed (message explains how many were metadata-only).
- Ordering: source matches by (file ordinal, line, column), then metadata by (container, kind, name) — stable across runs. Bounded 50 + `TotalMatchCount` + `Truncated` (reuse `CappedList`).
- Signature: `symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)` — pinned via snapshot tests so format drift is visible.
- Partial loads: search still runs on loaded projects; status `Partial` + failures carried (as in T-25).

## Metadata walk

Referenced assemblies only (project references are covered by the owning project's source search); Roslyn's name search never returns metadata symbols, so the collector walks assembly symbols itself.

- Dotted input resolves stepwise: the first segment is found anywhere in the namespace tree, later segments are name lookups on the resolved container (namespace members and nested types), so `Environment.SpecialFolder` finds `System.Environment.SpecialFolder`.
- A simple name finds namespaces and top-level types anywhere in the namespace tree; nested types are *not* found by a single-segment simple name, because the walk never enumerates type members — only name lookups (`GetNamespaceMembers()`, `GetTypeMembers(name)`).
- Deduplicated globally before capping: every project's compilation references the same assemblies, so the key is (assembly identity display name, fully-qualified dotted name, kind); candidates are key-sorted and the first per key is kept (duplicates are identical, so the result is run-order independent). The deduplicated set counts toward the cap, `TotalMatchCount` and `Truncated`, and the metadata-only count in the message is the deduplicated pre-cap count.

## Tool

- `cs_find_symbol`: schema `{ "name": string, required }`; description: "Find where a type or member with this name is defined and its signature — navigates large solutions without grepping". Read-only annotation. Text: status line + up to 10 inline `file:line — signature` lines; full list in Details. Lazy backend; freshness sync before search; statuses map to the same actionable messages as `cs_diagnostics`.

## Tests

- Fixture additions (both fixtures): a class with an overloaded method, a partial type, a nested type, a namespace-qualified symbol, and a metadata-only reference (e.g. a BCL type name like `System.String` is NOT in fixtures — metadata case tested with `List` style reference from a framework assembly: search "List" finds metadata `List` with FromMetadata; keep deterministic via fully qualified "System.Collections.Generic.List"? Simpler: search a BCL type name and assert ≥1 metadata match with FromMetadata=true, no file).
- Cases: type by name (file/line/signature correct); overloads → multiple matches distinct signatures; partial type → both files; dotted path; not found → empty + hint; case mismatch → empty + hint; metadata-only; truncation (generate >50 matches fixture? skip — assert cap logic via unit test of the collector instead); statuses pass-through on a NoSolution workspace; snapshot of the signature format; both cultures.
- Multi-project metadata: `String` in the lib-with-tests fixture yields exactly one metadata match; collector unit tests pin deduplication (also across project order), the pre-cap `MetadataMatchCount` (60 source + 1 metadata against the 50 cap) and nested dotted metadata (`Environment.SpecialFolder`, `System.Environment.SpecialFolder`).
- Lazy load: one probe per tool — `cs_diagnostics` and `cs_find_symbol` each construct and first-execute in their own child process, asserting no Roslyn/MSBuild assemblies before the first call.
- Warm latency: search on a loaded workspace fast (assert generous tripwire, disclosed; no new budget).

## Out of scope

`cs_find_references`, `cs_outline`, `cs_rename` (T-31); fuzzy/partial matching (agents pass exact names; fuzzy search is the grep tool's job); UI rendering.
